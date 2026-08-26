using System.Collections.Concurrent;

namespace Lertaro.Core.Services.Plugin.DirectoryIndex;

internal sealed class MonitoredDir
{
    public string Path { get; set; } = string.Empty;
    public bool Recursive { get; set; } = true;
    public string FilterPattern { get; set; } = "*";
}

// A registration's FilterPattern, in the "*.exe;*.lnk" form plugins register it in: split into the
// single patterns Directory.EnumerateFiles accepts one at a time, and matched with the same Win32
// wildcard semantics that call would apply, so an index-backed enumeration and a live filesystem walk
// of the same directory agree on which names the pattern selects.
public static class FilterPatternHelper
{
    public static string[] Split(string filterPattern)
    {
        if (string.IsNullOrWhiteSpace(filterPattern)) return new[] { "*" };
        var patterns = filterPattern.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return patterns.Length > 0 ? patterns : new[] { "*" };
    }

    // null = "everything matches", so a caller enumerating a whole subtree can skip per-name matching
    // outright instead of running a wildcard match that can only ever return true.
    public static string[]? SplitOrNullIfMatchAll(string? filterPattern)
    {
        var patterns = Split(filterPattern ?? string.Empty);
        return patterns.Any(IsMatchAll) ? null : patterns;
    }

    /// <summary>Combines registrations for one directory without losing either caller's file scope.</summary>
    public static string Combine(string existing, string incoming)
    {
        var patterns = Split(existing)
            .Concat(Split(incoming))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return patterns.Any(IsMatchAll) ? "*" : string.Join(';', patterns);
    }

    public static bool Matches(string name, string[] patterns)
    {
        foreach (var pattern in patterns)
        {
            // Translated first, exactly as Directory.EnumerateFiles does before matching
            // (FileSystemEnumerableFactory.NormalizeInputs): that is what turns "*.*" into "everything"
            // and the trailing dot of "*." into the DOS wildcard meaning "no extension". Matching the
            // raw expression would read both literally and quietly disagree with a live walk of the
            // same directory. IsMatchAll stays as a fast path for the pattern nearly everyone uses.
            if (IsMatchAll(pattern)
                || System.IO.Enumeration.FileSystemName.MatchesWin32Expression(
                    System.IO.Enumeration.FileSystemName.TranslateWin32Expression(pattern), name, ignoreCase: true))
                return true;
        }
        return false;
    }

    private static bool IsMatchAll(string pattern) => pattern is "*" or "*.*";
}

/// <summary>
/// Owns plugin directory registration and the FileSystemWatcher lifecycle backing it (create, retry on
/// disconnect/error, teardown on unregister). Kept separate from <see cref="PluginDirectorySearcher"/>,
/// which owns the actual local-vs-network query routing -- watching for changes and answering a search
/// are different responsibilities that only share the registration list.
/// </summary>
internal sealed class PluginDirectoryWatchRegistry
{
    private readonly ConcurrentDictionary<string, List<MonitoredDir>> _registrations = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, List<FileSystemWatcher>> _watchers = new(StringComparer.OrdinalIgnoreCase);
    // Every "this changed" from either a watcher below or an index goes through here, so a burst becomes
    // one notification and it lands after the index has caught up rather than before.
    private readonly PluginDirectoryChangeNotifier _notifier;

    public PluginDirectoryWatchRegistry() => _notifier = new PluginDirectoryChangeNotifier(AllRegistrations);

    /// <summary>Every (plugin, directory) pair currently registered -- what the notifier matches a changed source against.</summary>
    public IReadOnlyList<(string PluginId, string Path)> AllRegistrations()
    {
        var all = new List<(string, string)>();
        foreach (var (pluginId, dirs) in _registrations)
        {
            lock (dirs)
            {
                foreach (var dir in dirs)
                    all.Add((pluginId, dir.Path));
            }
        }
        return all;
    }

    public void RegisterDirectory(string pluginId, string directoryPath, bool recursive, string filterPattern)
    {
        if (string.IsNullOrWhiteSpace(directoryPath)) return;
        var fullPath = NormalizeDirectoryPath(directoryPath);

        var list = _registrations.GetOrAdd(pluginId, _ => new List<MonitoredDir>());
        lock (list)
        {
            var existing = list.FirstOrDefault(d => string.Equals(d.Path, fullPath, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                var includeSubdirectories = existing.Recursive || recursive;
                existing.FilterPattern = FilterPatternHelper.Combine(existing.FilterPattern, filterPattern);
                if (includeSubdirectories != existing.Recursive)
                {
                    existing.Recursive = true;
                    UpdateWatcherRecursion(pluginId, fullPath, recursive: true);
                }
                return;
            }

            list.Add(new MonitoredDir
            {
                Path = fullPath,
                Recursive = recursive,
                FilterPattern = filterPattern
            });
            Logger.Log($"[IndexManager] Plugin '{pluginId}' registered directory: '{fullPath}' (Recursive={recursive}, Filter={filterPattern})");

            // Set up FileSystemWatcher for monitoring changes and alerting the plugin via SDK event
            CreateWatcher(pluginId, fullPath, recursive, filterPattern);
            // Only worth listening to the indexes once somebody has a directory in them.
            if (!_notifier.EnsureIndexSubscriptions())
                _notifier.RefreshLocalIndexSubscription();
        }
    }

    public void UnregisterDirectories(string pluginId)
    {
        if (_registrations.TryRemove(pluginId, out _))
        {
            if (_watchers.TryRemove(pluginId, out var watcherList))
            {
                lock (watcherList)
                {
                    foreach (var w in watcherList)
                    {
                        try { w.Dispose(); } catch { }
                    }
                }
            }
            Logger.Log($"[IndexManager] Unregistered all directories for plugin '{pluginId}'.");
            if (_registrations.IsEmpty)
                _notifier.StopIndexSubscriptions();
            else
                _notifier.RefreshLocalIndexSubscription();
        }
    }

    /// <summary>A snapshot of the directories currently registered for a plugin, or null if none.</summary>
    public IReadOnlyList<MonitoredDir>? GetDirectories(string pluginId)
    {
        if (!_registrations.TryGetValue(pluginId, out var dirs))
            return null;
        lock (dirs)
        {
            return new List<MonitoredDir>(dirs);
        }
    }

    // All registrations pass through here, so syntactic variants of one directory never allocate two
    // watchers. Providers supply configured paths; the registry owns the key it stores and compares.
    internal static string NormalizeDirectoryPath(string directoryPath)
    {
        var fullPath = Path.GetFullPath(directoryPath);
        var root = Path.GetPathRoot(fullPath);
        return string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase)
            ? fullPath
            : Path.TrimEndingDirectorySeparator(fullPath);
    }

    private void CreateWatcher(string pluginId, string fullPath, bool recursive, string filterPattern)
    {
        if (!Directory.Exists(fullPath))
        {
            // If the folder is missing (disconnected drive), start reconnect loop
            _ = Task.Run(() => TryRecreateWatcherAsync(pluginId, fullPath, recursive, filterPattern));
            return;
        }

        try
        {
            var watcher = new FileSystemWatcher(fullPath)
            {
                IncludeSubdirectories = recursive,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
                EnableRaisingEvents = true
            };
            // Watch every name, then decide in the event handler whether a LastWrite fits the file
            // patterns. A watcher filter cannot express "all directories plus only matching files":
            // filtering it to *.mkv would silently miss creation, deletion and renaming of directories.
            watcher.Filters.Add("*");

            // Reported, not notified: a single file write raises several events on its own, and a bulk
            // copy raises thousands -- each of which used to invalidate the plugin's item cache and buy
            // a full re-listing of every directory it registered.
            FileSystemEventHandler handler = (s, e) =>
            {
                if (ShouldReportChange(pluginId, fullPath, e))
                    _notifier.Report(pluginId);
            };
            RenamedEventHandler renamedHandler = (s, e) => _notifier.Report(pluginId);

            watcher.Created += handler;
            watcher.Deleted += handler;
            watcher.Changed += handler;
            watcher.Renamed += renamedHandler;

            // Handle disconnection error by starting recovery loop
            watcher.Error += (s, e) =>
            {
                Logger.Log($"[IndexManager] Watcher error for '{fullPath}' (Plugin: {pluginId}): {e.GetException().Message}. Retrying...", LogLevel.Warn);
                RemoveWatcher(pluginId, watcher);
                _ = Task.Run(() => TryRecreateWatcherAsync(pluginId, fullPath, recursive, filterPattern));
            };

            var watcherList = _watchers.GetOrAdd(pluginId, _ => new List<FileSystemWatcher>());
            lock (watcherList)
            {
                watcherList.Add(watcher);
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"[IndexManager] Failed to start watcher for '{fullPath}': {ex.Message}", LogLevel.Warn);
            _ = Task.Run(() => TryRecreateWatcherAsync(pluginId, fullPath, recursive, filterPattern));
        }
    }

    private void RemoveWatcher(string pluginId, FileSystemWatcher watcher)
    {
        try { watcher.Dispose(); } catch { }
        if (_watchers.TryGetValue(pluginId, out var watcherList))
        {
            lock (watcherList)
            {
                watcherList.Remove(watcher);
            }
        }
    }

    private void UpdateWatcherRecursion(string pluginId, string fullPath, bool recursive)
    {
        if (!_watchers.TryGetValue(pluginId, out var watcherList))
            return;

        lock (watcherList)
        {
            foreach (var watcher in watcherList.Where(w => string.Equals(w.Path, fullPath, StringComparison.OrdinalIgnoreCase)))
                watcher.IncludeSubdirectories = recursive;
        }
    }

    private bool ShouldReportChange(string pluginId, string fullPath, FileSystemEventArgs change)
    {
        if (change.ChangeType != WatcherChangeTypes.Changed || string.IsNullOrEmpty(change.Name))
            return true;

        if (!_registrations.TryGetValue(pluginId, out var registrations))
            return true;

        lock (registrations)
        {
            var filterPattern = registrations.FirstOrDefault(d => string.Equals(d.Path, fullPath, StringComparison.OrdinalIgnoreCase))?.FilterPattern;
            var patterns = FilterPatternHelper.SplitOrNullIfMatchAll(filterPattern);
            return patterns == null || FilterPatternHelper.Matches(Path.GetFileName(change.Name), patterns);
        }
    }

    private async Task TryRecreateWatcherAsync(string pluginId, string fullPath, bool recursive, string filterPattern)
    {
        // Periodic check to self-heal when U-drives or network NAS comes back online
        while (true)
        {
            await Task.Delay(15000).ConfigureAwait(false); // Check every 15 seconds

            // Check if the plugin registration still exists (do not reconnect if unregistered)
            if (!_registrations.TryGetValue(pluginId, out var list))
                return;

            lock (list)
            {
                if (!list.Any(d => string.Equals(d.Path, fullPath, StringComparison.OrdinalIgnoreCase)))
                    return;
            }

            if (Directory.Exists(fullPath))
            {
                Logger.Log($"[IndexManager] Directory '{fullPath}' resolved back online. Re-creating FileSystemWatcher.");
                CreateWatcher(pluginId, fullPath, recursive, filterPattern);
                _notifier.Report(pluginId); // Force load newly connected drive contents
                return;
            }
        }
    }
}
