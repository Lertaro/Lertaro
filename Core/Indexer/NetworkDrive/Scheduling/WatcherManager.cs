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
    private readonly Func<string, bool> _directoryExists;
    private readonly TimeSpan? _retryDelay;
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

    // A watcher that died dropped every change it would otherwise have reported (see the Error handler in
    // ConfigureWatcher); this is what keeps that from queueing one refresh per failed retry.
    private readonly WatcherLossRefreshGate _lossGate = new();

    // One volume-removal registration per watched root, held exactly as long as that root's watcher is live.
    private readonly Dictionary<string, DriveDeviceRemovalMonitor?> _removals = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _removalsGate = new();

    public WatcherManager(
        Action<string, string> queueRefresh,
        Func<string, NetworkIndex?> getIndex,
        Action<string, NetworkIndex, IReadOnlyCollection<string>?> onIncrementalUpdate,
        Func<string, bool> markMissedIfRescanning,
        Func<string, bool>? directoryExists = null,
        TimeSpan? retryDelay = null)
    {
        _queueRefresh = queueRefresh;
        _getIndex = getIndex;
        _onIncrementalUpdate = onIncrementalUpdate;
        _markMissedIfRescanning = markMissedIfRescanning;
        _directoryExists = directoryExists ?? Directory.Exists;
        _retryDelay = retryDelay;
    }

    public void EnsureWatcher(string drive, bool needsInitialReconciliation = false)
    {
        // WSL UNC paths do not support ReadDirectoryChangesW/FileSystemWatcher (raises "Function incorrect" /
        // ERROR_INVALID_FUNCTION). Checked precisely (not every "\\wsl"-prefixed path) so a real UNC share
        // whose hostname happens to start with "wsl" (e.g. "\\wslbackup\share", indexable via the
        // folder-index feature) doesn't silently lose live change detection.
        if (WslPath.IsPath(drive))
            return;

        lock (_watchers)
        {
            if (_disposed || _watchers.ContainsKey(drive))
                return;

            DriveWatcherHost? host = null;
            host = new DriveWatcherHost(
                nameof(WatcherManager),
                drive,
                _directoryExists,
                ConfigureWatcher,
                Logger.Log,
                retryDelay: _retryDelay,
                onStarted: () =>
                {
                    // A live watcher is only safe to keep on a removable volume if a removal request can
                    // take it down in time, and a re-established one owes the tree a re-walk for whatever
                    // changed while nothing was watching.
                    WatchVolumeRemoval(drive, host!);
                    // The first scan can finish before monitoring starts, leaving an unobserved gap.
                    if (!_disposed && (_lossGate.Recovered(drive) || needsInitialReconciliation || _getIndex(drive) == null))
                        _queueRefresh(drive, "watcher recovered, reconcile the outage");
                },
                // An initially offline root has no watcher Error event. Remember that gap too,
                // so the first successful start reconciles files created before monitoring began.
                onUnavailable: () => _lossGate.ShouldRequestRefresh(drive));
            _watchers[drive] = host;
            // Directory.Exists and watcher startup may wait for a remote server. Never do that
            // on the settings/UI caller or while holding the manager's dictionary lock.
            _ = Task.Run(host.Start);
        }
    }

    public void RemoveWatcher(string drive)
    {
        DriveWatcherHost? watcher;
        lock (_watchers)
            _watchers.Remove(drive, out watcher);

        try
        {
            watcher?.Dispose();
        }
        catch
        {
        }

        // Remove ownership before taking the registration: a late background start can no longer
        // register a new removal handle after this cleanup has already passed.
        TakeRemovalMonitor(drive)?.Dispose();
        _lossGate.Recovered(drive);
        _publishDebounce.Cancel(drive);
    }

    // Arms the removal watch for a root whose watcher just went live. Ejecting the volume under a
    // folder-index root needs every handle on it gone by the time Windows asks, and asking is what this
    // registration buys: it costs one read handle of our own, which the answer closes again. A UNC share or
    // a mapped drive has no local volume to eject, so those roots are left alone.
    private void WatchVolumeRemoval(string drive, DriveWatcherHost host)
    {
        var letter = EjectableVolumeOf(PathHelpers.BuildSourceRoot(drive));
        if (letter == null)
            return;

        // Re-armed on every (re)start: the registration that answered the last removal request closed its
        // own handle and can never report again for this root.
        DriveDeviceRemovalMonitor? previous;
        lock (_watchers)
        {
            if (_disposed || !_watchers.TryGetValue(drive, out var current) || !ReferenceEquals(current, host))
                return;
            previous = TakeRemovalMonitor(drive);
            lock (_removalsGate)
                _removals[drive] = DriveDeviceRemovalMonitor.Register(letter, () => ReleaseForRemoval(drive), () => { }, () => { });
        }
        previous?.Dispose();
    }

    // Answers the removal request by giving the handle up: the watcher goes, its now-deaf registration goes
    // with it, and the outage is recorded so the re-arm re-walks the tree. DriveWatcherHost's own polling is
    // what brings all of this back -- the volume is either still mounted (somebody else vetoed the eject) or
    // will be re-inserted.
    private void ReleaseForRemoval(string drive)
    {
        if (_disposed)
            return;

        _ = _lossGate.ShouldRequestRefresh(drive);
        TakeRemovalMonitor(drive)?.Dispose();

        DriveWatcherHost? host;
        lock (_watchers)
            _watchers.TryGetValue(drive, out host);
        host?.Release();
    }

    private DriveDeviceRemovalMonitor? TakeRemovalMonitor(string drive)
    {
        lock (_removalsGate)
        {
            if (!_removals.TryGetValue(drive, out var monitor))
                return null;
            _removals.Remove(drive);
            return monitor;
        }
    }

    // The volume an eject would be about: a local letter that is not a redirected (network) root. A USB disk
    // reports Fixed and a stick reports Removable, so the only root excluded here is a share -- whose volume
    // is not this machine's to release. DriveInfo throws for the UNC spelling, folded into "no volume" here.
    private static string? EjectableVolumeOf(string path)
    {
        try
        {
            var root = Path.GetPathRoot(path);
            if (string.IsNullOrEmpty(root) || root.StartsWith(@"\\", StringComparison.Ordinal))
                return null;

            return new DriveInfo(root).DriveType == DriveType.Network
                ? null
                : root[..1].ToUpperInvariant();
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or System.Security.SecurityException)
        {
            return null;
        }
    }

    // Coalesces however many watcher events land on this drive within PublishDebounceMs into a single
    // publish -- resetting an existing pending timer rather than letting both fire, so a steady stream of
    // changes (e.g. a large copy in progress) never actually reaches the timer's due time until it stops.
    // Every event folded into the pending publish contributes its affected directory, because the debounce means
    // one publish stands for all of them: dropping the ones that were coalesced away would tell a
    // subscriber only about the last file of a copy and leave it believing the rest never happened.
    private readonly Dictionary<string, HashSet<string>> _pendingDirectories = new(StringComparer.OrdinalIgnoreCase);

    private void SchedulePublish(string drive, NetworkIndex index, params string?[] changedDirectories)
    {
        lock (_pendingDirectories)
        {
            if (!_pendingDirectories.TryGetValue(drive, out var pending))
                _pendingDirectories[drive] = pending = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var path in changedDirectories)
            {
                if (!string.IsNullOrEmpty(path))
                    pending.Add(path);
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
            TakeRemovalMonitor(drive)?.Dispose();
            _publishDebounce.Cancel(drive);

            // The watcher is gone, and every change it failed to deliver (the classic case is a buffer
            // overflow on a busy share) is only recoverable by re-walking the drive: the retry below resumes
            // live monitoring, it never replays the gap. Gated per loss episode (WatcherLossRefreshGate) so
            // a sustained outage -- each failed retry can raise its own error -- queues one refresh.
            if (!_disposed && _lossGate.ShouldRequestRefresh(drive))
                _queueRefresh(drive, "watcher lost, changes may have been missed");

            // Keep the same host and its single retry loop, including before the first index
            // exists. Removing this configured root disposes the host and stops these retries.
            restart();
        };
        return true;
    }

    private string TranslateToLogical(string drive, string path) => path;

    private void OnWatcherChanged(string drive, WatcherChangeTypes changeType, string path)
    {
        try
        {
            var changed = false;
            var isDirectory = false;
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
                isDirectory = Directory.Exists(path);
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
                    SchedulePublish(drive, index, GetChangedDirectory(logicalPath, isDirectory));
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
                    SchedulePublish(
                        drive,
                        index,
                        Path.GetDirectoryName(logicalOldPath),
                        GetChangedDirectory(logicalNewPath, newIsDirectory));
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"[WatcherManager] Watcher rename handling failed on {drive}: {ex.Message}", LogLevel.Error);
            _queueRefresh(drive, "incremental rename failure");
        }
    }

    private static string? GetChangedDirectory(string path, bool isDirectory)
        => isDirectory ? path : Path.GetDirectoryName(path);

    public void Dispose()
    {
        _disposed = true;

        // First the registrations, so no removal request can arrive while the watchers are being torn down.
        List<DriveDeviceRemovalMonitor?> removals;
        lock (_removalsGate)
        {
            removals = _removals.Values.ToList();
            _removals.Clear();
        }
        foreach (var removal in removals)
            removal?.Dispose();

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
