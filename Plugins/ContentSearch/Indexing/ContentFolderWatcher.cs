using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.ContentSearch.Indexing;

/// <summary>
/// Observes file system changes by registering monitored directories with the host's DirectoryIndexerService SDK service.
/// </summary>
public sealed class ContentFolderWatcher : IDisposable
{
    private const string PluginId = "Lertaro.Plugins.ContentSearch";
    private readonly Action _onFoldersChanged;
    private IDisposable? _watchSubscription;
    private readonly object _lock = new();

    public ContentFolderWatcher(Action onFoldersChanged)
    {
        _onFoldersChanged = onFoldersChanged;
        _watchSubscription = DirectoryIndexerService.WatchDirectories(PluginId, _onFoldersChanged);
    }

    public void UpdateFolders(IEnumerable<string> folders, string filterPattern = "*")
    {
        lock (_lock)
        {
            DirectoryIndexerService.UnregisterDirectories(PluginId);

            if (string.IsNullOrEmpty(filterPattern))
                return;

            foreach (var rawFolder in folders)
            {
                var folder = ContentIndexScheduler.NormalizeFolderPath(rawFolder);
                if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
                {
                    DirectoryIndexerService.RegisterDirectory(PluginId, folder, recursive: true, filterPattern: filterPattern);
                }
            }
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _watchSubscription?.Dispose();
            _watchSubscription = null;
            DirectoryIndexerService.UnregisterDirectories(PluginId);
        }
    }
}
