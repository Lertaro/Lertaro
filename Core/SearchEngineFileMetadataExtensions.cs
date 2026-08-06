using Lertaro.Core.IndexV2;

using Lertaro.Core.IndexV2.Search;

namespace Lertaro.Core;

// Backs the GetFileMetadata pipe request: looks up Size/Created/Modified/Accessed straight out of
// the in-memory index (no disk I/O) for whichever of the given paths are actually indexed. Paths
// that aren't found (not yet scanned, on an unindexed drive, etc) are simply omitted -- the client
// falls back to a live filesystem stat for those.
//
// Faithfully ports the old engine's exact resolution shape, including its existing quirk: path
// resolution walks DIRECTORY children only (DirectoryFilterResolver.TryResolve, like the old
// TryResolvePath), so a path only resolves here when every segment including the last names a
// directory -- a plain file path's last segment lands in `remainder` and is skipped. Preserved
// as-is rather than "fixed" during migration; changing it is a separate, deliberate decision.
public static class SearchEngineFileMetadataExtensions
{
    public static Dictionary<string, FileMetadataEntry> GetFileMetadataBatch(this Indexer.Usn.UsnIndexer indexer, IReadOnlyList<string> paths)
    {
        var result = new Dictionary<string, FileMetadataEntry>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, LiveIndex> drives;
        lock (indexer.LockObj)
        {
            drives = new Dictionary<string, LiveIndex>(indexer._recordIndexes, StringComparer.OrdinalIgnoreCase);
        }

        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path))
                continue;

            var root = Path.GetPathRoot(path);
            if (string.IsNullOrEmpty(root) || !char.IsLetter(root[0]))
                continue;

            var drive = root[0].ToString().ToUpperInvariant();
            if (!drives.TryGetValue(drive, out var live))
                continue;

            var pathLower = path.ToLowerInvariant();
            var entry = live.Read((snapshot, delta) =>
            {
                if (!DirectoryFilterResolver.TryResolve(snapshot, delta, pathLower, forceLastSegmentAsQuery: false, out var row, out var remainder) || remainder.Length > 0)
                    return (FileMetadataEntry?)null;
                var (size, creation, lastWrite, lastAccess) = delta.MetadataOf(row);
                return new FileMetadataEntry(size, creation, lastWrite, lastAccess);
            });

            if (entry.HasValue)
                result[path] = entry.Value;
        }
        return result;
    }
}
