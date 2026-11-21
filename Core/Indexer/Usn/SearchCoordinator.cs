using Lertaro.PluginSdk.Helpers;
using Lertaro.Core.IndexV2;

using Lertaro.Core.IndexV2.Search;
using Lertaro.Core.SearchIndex;

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
        string? fileNameFilter = null,
        SearchCategoryFilter category = SearchCategoryFilter.All)
    {
        KeyValuePair<string, LiveIndex>[] drives;
        lock (lockObj)
        {
            drives = recordIndexes.ToArray();
        }

        if (drives.Length == 0 || limit <= 0)
            return;

        if (drives.Length == 1)
        {
            IndexV2Searcher.SearchStreaming(drives[0].Value, query, limit, onResult, token, directoryFilter, fileNameFilter, category);
            return;
        }

        // For bounded queries, collect each drive's best limit rows and rank their union so a fast drive
        // cannot consume the whole budget. Preserve other drives' results if one fails; cancellation
        // still ends the query.
        //
        // ponytail: buffering costs drives × limit rows and delays the first result until the slowest
        // drive returns. Unbounded Full Window queries retain streaming. Incremental top-N merging could
        // reduce bounded-query latency.
        if (limit < int.MaxValue)
        {
            var buffered = new List<SearchResult>[drives.Length];
            Parallel.For(0, drives.Length, ParallelOptionsFor(drives.Length, token), i =>
            {
                var rows = buffered[i] = new List<SearchResult>();
                try
                {
                    IndexV2Searcher.SearchStreaming(drives[i].Value, query, limit, rows.Add, token, directoryFilter, fileNameFilter, category);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Logger.Log($"[SearchCoordinator] Search of drive {drives[i].Key} failed; its rows are left out: {ex.Message}", LogLevel.Error);
                }
            });
            foreach (var row in buffered.SelectMany(rows => rows).OrderBy(row => row, SearchCandidateComparer.Instance).Take(limit))
            {
                token.ThrowIfCancellationRequested();
                onResult(row);
            }
            return;
        }

        Parallel.For(0, drives.Length, ParallelOptionsFor(drives.Length, token), i =>
            // Emits with no lock held. Wrapping onResult in a lock serialised every drive's emission behind
            // whichever pipe write is currently parked on the pump's backpressure, so one slow client
            // stalled every other drive as well. The wire itself stays serialised -- the pump has one
            // consumer task writing the response -- but the callback is reachable concurrently, which is
            // why SearchStreamPump.CreateResultChannel sets SingleWriter = false. Cross-drive arrival order
            // is not meaningful: consumers merge by the shared RankSortKey.
            IndexV2Searcher.SearchStreaming(drives[i].Value, query, limit, onResult, token, directoryFilter, fileNameFilter, category));
    }

    private static ParallelOptions ParallelOptionsFor(int driveCount, CancellationToken token) => new()
    {
        CancellationToken = token,
        MaxDegreeOfParallelism = Math.Min(driveCount, Math.Clamp(Environment.ProcessorCount, 2, 8))
    };

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

    // Per-drive query page caches live on each LiveIndex; the idle/activity release paths clear them
    // through UsnIndexer.ClearCaches. This no-arg hook remains for callers that only hold the
    // coordinator -- there is no cross-drive cache here to clear.
    public static void ClearCaches()
    {
    }

    public static void ClearCaches(Dictionary<string, LiveIndex> recordIndexes, object lockObj)
    {
        LiveIndex[] drives;
        lock (lockObj)
        {
            drives = recordIndexes.Values.ToArray();
        }
        foreach (var drive in drives)
            drive.SearchPages.Clear();
    }
}
