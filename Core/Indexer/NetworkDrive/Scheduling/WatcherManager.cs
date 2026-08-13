using Lertaro.Core.DriveMonitoring;
using Lertaro.Core.Indexer.NetworkDrive.Walk;
namespace Lertaro.Core.Indexer.NetworkDrive.Scheduling;

internal class WatcherManager : IDisposable
{
    private readonly Dictionary<string, DriveWatcherHost> _watchers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Action<string, string> _queueRefresh;
    private readonly Func<string, NetworkIndex?> _getIndex;
    private readonly Action<string, NetworkIndex, IReadOnlyCollection<string>?> _onIncrementalUpdate;
    private readonly Func<string, bool> _markMissedIfRescanning;
    private volatile bool _disposed;

    // Every raw FileSystemWatcher event that changed the in-memory index used to trigger _onIncrementalUpdate
    // immediately -- which persists via NetworkIndexerPublisher.PublishIncrementalUpdate -> LiveIndex.
    // Compact(force: true), a synchronous FULL snapshot rewrite -- with zero throttling. A share under
    // active, ongoing write traffic (a team drive, a build output folder) could trigger one multi-hundred-MB
    // rewrite PER FILE CHANGE. ApplyCreatedOrChanged/ApplyDeleted above still update the in-memory delta
    // immediately (live search stays current); only the expensive disk persist is debounced per drive here,
    // so a burst of changes collapses into one rewrite once that drive goes quiet for a bit.
    private const int PublishDebounceMs = 1000;
    private readonly KeyedDebouncer<string> _publishDebounce = new(PublishDebounceMs, StringComparer.OrdinalIgnoreCase);

    public WatcherManager(
        Action<string, string> queueRefresh,
        Func<string, NetworkIndex?> getIndex,
        Action<string, NetworkIndex, IReadOnlyCollection<string>?> onIncrementalUpdate,
        Func<string, bool> markMissedIfRescanning)
    {
        _queueRefresh = queueRefresh;
        _getIndex = getIndex;
        _onIncrementalUpdate = onIncrementalUpdate;
        _markMissedIfRescanning = markMissedIfRescanning;
    }

    public void EnsureWatcher(string drive)
    {
        // WSL UNC paths do not support ReadDirectoryChangesW/FileSystemWatcher (raises "Function incorrect" /
        // ERROR_INVALID_FUNCTION). Checked precisely (not every "\\wsl"-prefixed path) so a real UNC share
        // whose hostname happens to start with "wsl" (e.g. "\\wslbackup\share", indexable via the
        // folder-index feature) doesn't silently lose live change detection.
        if (WslPath.IsPath(drive))
            return;

        lock (_watchers)
        {
            if (_watchers.ContainsKey(drive))
                return;

            var host = new DriveWatcherHost(
                nameof(WatcherManager),
                drive,
                Directory.Exists,
                ConfigureWatcher,
                message => Logger.Log(message, LogLevel.Info));
            _watchers[drive] = host;
            host.Start();
        }
    }

    public void RemoveWatcher(string drive)
    {
        lock (_watchers)
        {
            if (_watchers.Remove(drive, out var watcher))
            {
                try
                {
                    watcher.Dispose();
                }
                catch
                {
                }
            }
        }

        _publishDebounce.Cancel(drive);
    }

    // Coalesces however many watcher events land on this drive within PublishDebounceMs into a single
    // publish -- resetting an existing pending timer rather than letting both fire, so a steady stream of
    // changes (e.g. a large copy in progress) never actually reaches the timer's due time until it stops.
    // Every event folded into the pending publish contributes its directory, because the debounce means
    // one publish stands for all of them: dropping the ones that were coalesced away would tell a
    // subscriber only about the last file of a copy and leave it believing the rest never happened.
    private readonly Dictionary<string, HashSet<string>> _pendingDirectories = new(StringComparer.OrdinalIgnoreCase);

    private void SchedulePublish(string drive, NetworkIndex index, params string[] changedPaths)
    {
        lock (_pendingDirectories)
        {
            if (!_pendingDirectories.TryGetValue(drive, out var pending))
                _pendingDirectories[drive] = pending = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var path in changedPaths)
            {
                if (!string.IsNullOrEmpty(path))
                    pending.Add(path);
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                    pending.Add(directory);
            }
        }

        _publishDebounce.Schedule(drive, () => _onIncrementalUpdate(drive, index, TakePendingDirectories(drive)));
    }

    // Past this a burst is bulk activity across the share rather than something a subscriber could be
    // told precisely, and "somewhere, unknown" is both honest and cheaper than a list nobody can act on.
    private const int MaxPendingDirectories = 64;

    private IReadOnlyCollection<string>? TakePendingDirectories(string drive)
    {
        lock (_pendingDirectories)
        {
            if (!_pendingDirectories.Remove(drive, out var pending))
                return Array.Empty<string>();
            return pending.Count > MaxPendingDirectories ? null : pending;
        }
    }

    private bool ConfigureWatcher(FileSystemWatcher watcher, string drive, Action restart, Action retry, Action<string> logError)
    {
        watcher.IncludeSubdirectories = true;
        watcher.InternalBufferSize = 64 * 1024;
        watcher.NotifyFilter = NotifyFilters.FileName |
                               NotifyFilters.DirectoryName |
                               NotifyFilters.LastWrite |
                               NotifyFilters.Size |
                               NotifyFilters.Attributes |
                               NotifyFilters.CreationTime;
        FileSystemEventHandler onChanged = (_, e) => OnWatcherChanged(drive, e.ChangeType, e.FullPath);
        RenamedEventHandler onRenamed = (_, e) => OnWatcherRenamed(drive, e.OldFullPath, e.FullPath);
        watcher.Created += onChanged;
        watcher.Changed += onChanged;
        watcher.Deleted += onChanged;
        watcher.Renamed += onRenamed;
        watcher.Error += (_, e) =>
        {
            var ex = e.GetException();
            logError($"Watcher error on {drive}: {ex?.Message ?? "unknown"}");
            RemoveWatcher(drive);

            if (_getIndex(drive) != null)
            {
                // Existing index is still valid; keep retrying until the watcher comes back up.
                // ponytail: fixed 10 s back-off; upgrade to exponential if flapping becomes an issue.
                _ = Task.Run(async () =>
                {
                    while (!_disposed)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                        if (_disposed)
                            break;
                        EnsureWatcher(drive);
                        lock (_watchers)
                        {
                            if (_watchers.ContainsKey(drive))
                                break;
                        }
                    }
                });
            }
            else
            {
                _queueRefresh(drive, "watcher error");
            }
        };
        return true;
    }

    private string TranslateToLogical(string drive, string path) => path;

    private void OnWatcherChanged(string drive, WatcherChangeTypes changeType, string path)
    {
        try
        {
            var changed = false;
            var index = _getIndex(drive);

            if (index == null)
            {
                _queueRefresh(drive, "missing index");
                return;
            }

            var logicalPath = TranslateToLogical(drive, path);
            if (changeType == WatcherChangeTypes.Deleted)
            {
                changed = index.ApplyDeleted(logicalPath);
            }
            else
            {
                var exclusionRules = ExclusionRuleSet.From(UserSettings.Load());
                var isDirectory = Directory.Exists(path);
                if (exclusionRules.IsExcludedPath(logicalPath, isDirectory))
                    changed = index.ApplyDeleted(logicalPath);
                else
                    changed = index.ApplyCreatedOrChanged(PathHelpers.BuildSourceRoot(drive), logicalPath, exclusionRules);
            }

            if (changed)
            {
                Logger.Log($"[WatcherManager] Incremental {changeType} applied on {drive}: {logicalPath}; items={index.Count}", LogLevel.Debug);
                // Checked synchronously, right here, rather than leaving it to PublishIncrementalUpdate's
                // own debounced timer to discover later -- see NetworkIndexerPublisher.MarkMissedIfRescanning's
                // own comment for why the late-only check could miss this drive's rescan finishing (and
                // the missed flag with it) inside the debounce window.
                if (!_markMissedIfRescanning(drive))
                    SchedulePublish(drive, index, logicalPath);
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"[WatcherManager] Watcher changed handling failed on {drive}: {ex.Message}", LogLevel.Error);
            _queueRefresh(drive, "incremental failure");
        }
    }

    private void OnWatcherRenamed(string drive, string oldPath, string newPath)
    {
        try
        {
            var index = _getIndex(drive);

            if (index == null)
            {
                _queueRefresh(drive, "missing index");
                return;
            }

            var logicalOldPath = TranslateToLogical(drive, oldPath);
            var logicalNewPath = TranslateToLogical(drive, newPath);
            var exclusionRules = ExclusionRuleSet.From(UserSettings.Load());
            var newIsDirectory = Directory.Exists(newPath);
            var changed = index.ApplyDeleted(logicalOldPath);
            if (!exclusionRules.IsExcludedPath(logicalNewPath, newIsDirectory))
                changed |= index.ApplyCreatedOrChanged(PathHelpers.BuildSourceRoot(drive), logicalNewPath, exclusionRules);

            if (changed)
            {
                Logger.Log($"[WatcherManager] Incremental Rename applied on {drive}: {logicalOldPath} -> {logicalNewPath}; items={index.Count}", LogLevel.Debug);
                if (!_markMissedIfRescanning(drive))
                    SchedulePublish(drive, index, logicalOldPath, logicalNewPath);
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"[WatcherManager] Watcher rename handling failed on {drive}: {ex.Message}", LogLevel.Error);
            _queueRefresh(drive, "incremental rename failure");
        }
    }

    public void Dispose()
    {
        _disposed = true;
        lock (_watchers)
        {
            foreach (var watcher in _watchers.Values)
            {
                try
                {
                    watcher.Dispose();
                }
                catch
                {
                }
            }
            _watchers.Clear();
        }

        _publishDebounce.Dispose();
    }
}
