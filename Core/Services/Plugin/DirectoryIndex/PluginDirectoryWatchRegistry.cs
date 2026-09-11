using System.Collections.Concurrent;

namespace Lertaro.Core.Services.Plugin.DirectoryIndex;

internal sealed class MonitoredDir
{
    public string Path { get; set; } = string.Empty;
    public bool Recursive { get; set; } = true;
    public string FilterPattern { get; set; } = "*";
}

/// <summary>
/// Owns plugin directory registration. Change notifications come from the host indexes through
/// <see cref="PluginDirectoryChangeNotifier"/>; a second FileSystemWatcher per plugin registration would
/// duplicate the host's USN, network-drive, WSL, and folder-index monitoring.
/// </summary>
internal sealed class PluginDirectoryWatchRegistry
{
    private readonly ConcurrentDictionary<string, List<MonitoredDir>> _registrations = new(StringComparer.OrdinalIgnoreCase);
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
                    existing.Recursive = true;
                return;
            }

            list.Add(new MonitoredDir
            {
                Path = fullPath,
                Recursive = recursive,
                FilterPattern = filterPattern
            });
            Logger.Log($"[IndexManager] Plugin '{pluginId}' registered directory: '{fullPath}' (Recursive={recursive}, Filter={filterPattern})");

            // Only worth listening to the indexes once somebody has a directory registered.
            if (!_notifier.EnsureIndexSubscriptions())
                _notifier.RefreshLocalIndexSubscription();
        }
    }

    public void UnregisterDirectories(string pluginId)
    {
        if (_registrations.TryRemove(pluginId, out _))
        {
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
}
