using Lertaro.Core;
using Lertaro.PluginSdk.Abstractions;

using Lertaro.Core.Services.Search;
namespace Lertaro.App.Services;

// Backs PluginSdk's FileMetadataService for plugins: ask the Service process's in-memory index
// first (no disk I/O), then fall back to a live filesystem stat for whatever it doesn't track.
internal static class FileMetadataBridge
{
    private static readonly DateTime UnixEpoch = new(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public static async Task<IReadOnlyDictionary<string, FileMetadata>> GetMetadataBatchAsync(IReadOnlyList<string> paths)
    {
        var result = new Dictionary<string, FileMetadata>(StringComparer.OrdinalIgnoreCase);
        if (paths.Count == 0)
            return result;

        using var searchService = new SearchService();
        var indexed = await searchService.GetFileMetadataBatchAsync(paths);

        var missing = new List<string>();
        foreach (var path in paths)
        {
            if (indexed.TryGetValue(path, out var entry))
                result[path] = ToFileMetadata(entry);
            else
                missing.Add(path);
        }

        if (missing.Count > 0)
            await Task.Run(() => StatFallback(missing, result));

        return result;
    }

    private static FileMetadata ToFileMetadata(FileMetadataEntry entry) => new(
        entry.Size,
        UnixEpoch.AddSeconds(entry.CreationTimeUnixSeconds).ToLocalTime(),
        UnixEpoch.AddSeconds(entry.LastWriteTimeUnixSeconds).ToLocalTime(),
        UnixEpoch.AddSeconds(entry.LastAccessTimeUnixSeconds).ToLocalTime());

    private static void StatFallback(List<string> paths, Dictionary<string, FileMetadata> result)
    {
        foreach (var path in paths)
        {
            try
            {
                if (System.IO.Directory.Exists(path))
                {
                    var info = new System.IO.DirectoryInfo(path);
                    lock (result) result[path] = new FileMetadata(0, info.CreationTime, info.LastWriteTime, info.LastAccessTime);
                }
                else if (System.IO.File.Exists(path))
                {
                    var info = new System.IO.FileInfo(path);
                    lock (result) result[path] = new FileMetadata(info.Length, info.CreationTime, info.LastWriteTime, info.LastAccessTime);
                }
            }
            catch
            {
                // Path vanished mid-lookup or is otherwise unreadable -- just omit it.
            }
        }
    }
}
