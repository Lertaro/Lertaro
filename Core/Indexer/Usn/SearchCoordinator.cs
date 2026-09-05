using Lertaro.Core.IndexV2;

using Lertaro.Core.IndexV2.Search;

namespace Lertaro.Core.Indexer.Usn;

// Fans a query out across every local drive's LiveIndex. Unlike the old RuntimeIndex-based coordinator
// (which held UsnIndexer's single coarse lock for the ENTIRE search, serializing all drives' searches
// against each other AND against USN update application), each LiveIndex now owns its own
// reader-writer lock -- so the outer lock here only protects the brief `.ToArray()` snapshot of which
// drives currently exist, not the search work itself. Strictly finer-grained than before, never coarser.
internal static class SearchCoordinator
{
    public static void SearchStreaming(
        Dictionary<string, LiveIndex> recordIndexes,
        object lockObj,
        string query,
        int limit,
        Action<SearchResult> onResult,
        CancellationToken token,
        string? directoryFilter,
        string? fileNameFilter = null)
    {
        LiveIndex[] drives;
        lock (lockObj)
        {
            drives = recordIndexes.Values.ToArray();
        }

        if (drives.Length == 0)
            return;

        if (drives.Length == 1)
        {
            IndexV2Searcher.SearchStreaming(drives[0], query, limit, onResult, token, directoryFilter, fileNameFilter);
            return;
        }

        var writeLock = new object();
        Parallel.For(
            0,
            drives.Length,
            new ParallelOptions
            {
                CancellationToken = token,
                MaxDegreeOfParallelism = Math.Min(drives.Length, Math.Clamp(Environment.ProcessorCount, 2, 8))
            },
            i =>
            {
                token.ThrowIfCancellationRequested();
                IndexV2Searcher.SearchStreaming(drives[i], query, limit, result =>
                {
                    token.ThrowIfCancellationRequested();
                    lock (writeLock)
                    {
                        token.ThrowIfCancellationRequested();
                        onResult(result);
                    }
                }, token, directoryFilter, fileNameFilter);
            });
    }

    // No fan-out here, unlike the search above: a path lives on exactly one drive, and every other
    // drive's index rejects it on the source-root prefix check before doing any work.
    public static bool EnumerateDirectory(
        Dictionary<string, LiveIndex> recordIndexes,
        object lockObj,
        string path,
        bool recursive,
        string[]? patterns,
        int limit,
        Action<SearchResult> onResult,
        CancellationToken token)
    {
        LiveIndex[] drives;
        lock (lockObj)
        {
            drives = recordIndexes.Values.ToArray();
        }

        foreach (var drive in drives)
        {
            token.ThrowIfCancellationRequested();
            if (IndexV2Searcher.EnumerateDirectory(drive, path, recursive, patterns, limit, onResult, token))
                return true;
        }
        return false;
    }

    // IndexV2 has no cross-search rank/candidate cache yet (a known follow-up, not a correctness gap
    // -- see the IndexV2 migration notes); kept as a no-op call site so callers don't need to know that.
    public static void ClearCaches()
    {
    }
}
