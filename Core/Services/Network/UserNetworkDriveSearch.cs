using Lertaro.Core.Indexer.NetworkDrive;

namespace Lertaro.Core.Services.Network;

public static class UserNetworkDriveSearch
{
    private static readonly NetworkIndexer NetworkIndexer = new();
    public static event Action<IReadOnlyList<NetworkIndexStatus>>? StatusesChanged
    {
        add => NetworkIndexer.StatusesChanged += value;
        remove => NetworkIndexer.StatusesChanged -= value;
    }

    /// <summary>Where a network, WSL or folder index just changed -- see NetworkIndexer.</summary>
    public static event Action<string, IReadOnlyCollection<string>?>? DirectoriesChanged
    {
        add => NetworkIndexer.DirectoriesChanged += value;
        remove => NetworkIndexer.DirectoriesChanged -= value;
    }

    public static void Configure()
    {
        var settings = UserSettings.Load();
        NetworkIndexer.Configure(settings.NetworkDrives, settings.WslSettings, settings.FolderIndexes);
    }

    public static void Refresh()
    {
        var settings = UserSettings.Load();
        NetworkIndexer.Configure(settings.NetworkDrives, settings.WslSettings, settings.FolderIndexes, forceRefresh: true);
    }
    public static bool RefreshDrive(string drive) => NetworkIndexer.RefreshDrive(drive);
    public static bool CancelDrive(string drive) => NetworkIndexer.CancelDrive(drive);

    public static IReadOnlyList<NetworkIndexStatus> GetStatuses() => NetworkIndexer.GetStatuses();
    public static bool HasCache(string drive) => IndexerHelper.HasCache(drive);
    public static IReadOnlyList<string> GetCachedDrives() => IndexerHelper.GetCachedDrives();
    public static void DeleteCache(string drive) => NetworkIndexer.DeleteCache(drive);
    public static void ClearAllCaches() => NetworkIndexer.ClearAllCaches();


    public static void SearchStreaming(string query, int limit, Action<SearchResult> onResult, CancellationToken token = default, string? directoryFilter = null) => NetworkIndexer.SearchStreaming(query, limit, onResult, token, directoryFilter);

    // In-process counterpart of SearchEngine.EnumerateDirectory (which only ever sees local drives):
    // lists a directory out of whichever network/WSL/folder index holds it. False = none of them does.
    public static bool EnumerateDirectory(string path, bool recursive, string filterPattern, int limit, Action<SearchResult> onResult, CancellationToken token = default)
        => NetworkIndexer.EnumerateDirectory(path, recursive, Plugin.DirectoryIndex.FilterPatternHelper.SplitOrNullIfMatchAll(filterPattern), limit, onResult, token);

    public static List<SearchResult> GetRecentFiles(IReadOnlyList<string> directories, int limit, int maxAgeMinutes) => NetworkIndexer.GetRecentFiles(directories, limit, maxAgeMinutes);
}
