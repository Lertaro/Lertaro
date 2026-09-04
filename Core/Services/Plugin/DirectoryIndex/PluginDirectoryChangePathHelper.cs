namespace Lertaro.Core.Services.Plugin.DirectoryIndex;

// Split out purely to keep PluginDirectoryWatchRegistry under the repository's per-file line limit;
// this helper has no state and only translates watcher paths into safe directory scopes.
internal static class PluginDirectoryChangePathHelper
{
    public static void AddChangedDirectory(HashSet<string> changedDirectories, string path, WatcherChangeTypes changeType)
    {
        var directory = ResolveChangedDirectory(path, changeType);
        if (!string.IsNullOrEmpty(directory))
            changedDirectories.Add(directory);
    }

    public static string? ResolveChangedDirectory(string path, WatcherChangeTypes changeType)
    {
        if (string.IsNullOrEmpty(path))
            return null;

        // FileSystemWatcher does not preserve the type of a deleted entry. A deleted path therefore
        // invalidates its parent, which is correct for both a deleted file and a deleted directory.
        if (changeType == WatcherChangeTypes.Deleted)
            return Path.GetDirectoryName(path);

        return Directory.Exists(path) ? path : Path.GetDirectoryName(path);
    }
}
