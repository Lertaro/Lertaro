using Lertaro.PluginSdk.Helpers;
using Lertaro.Core.SearchIndex.Fzf;

using Lertaro.Core.IndexV2.Search.PathMode;
using Lertaro.Core.SearchIndex.Query;
using Lertaro.Core.Services.Plugin.DirectoryIndex;
namespace Lertaro.Core.IndexV2.Search;

// Top-level search entry point for a single drive's LiveIndex: parse the query, route path-mode queries
// to PathSearch and everything else to NameSearch, normalize/gate the directory filter. Runs entirely
// inside one LiveIndex.Read call so
// the whole search sees one consistent (Snapshot, DeltaOverlay) pair.
public static class IndexV2Searcher
{
    public static void SearchStreaming(LiveIndex index, string query, int limit, Action<SearchResult> onResult, CancellationToken token, string? directoryFilter = null, string? fileNameFilter = null, SearchCategoryFilter category = SearchCategoryFilter.All)
    {
        if (limit <= 0 || string.IsNullOrWhiteSpace(query))
            return;

        var parsed = SearchQueryParser.Parse(query);
        var cacheKey = parsed.IsPathMode
            ? null
            : QueryPageCache.KeyFor(query, directoryFilter, fileNameFilter, category);
        if (cacheKey != null && index.SearchPages.TryReplay(cacheKey, limit, onResult, token, index.Revision))
            return;

        var captured = cacheKey == null ? null : new List<SearchResult>(Math.Min(limit, QueryPageCache.MaxRows));
        var emitted = 0;
        void Capture(SearchResult row)
        {
            emitted++;
            if (captured != null && captured.Count < QueryPageCache.MaxRows)
                captured.Add(row);
            onResult(row);
        }

        // ponytail: the whole search, every onResult callback included, runs inside this read lock. The
        // production callback is a blocking write to SearchStreamPump's bounded channel, so one slow pipe
        // client parks the search thread holding the lock -- and ReaderWriterLockSlim blocks new readers
        // while a writer waits, so LiveIndex.Mutate (USN/watcher apply) and Compact queue behind it: a
        // stalled client can stall indexing for that drive. Deferred deliberately (decided 2026-09-22),
        // because the fix is not local: collecting the page inside the lock and emitting after
        // ExitReadLock removes progressive streaming for large result sets -- rows arrive all at once --
        // and needs a memory bound the full window cannot state while its limit is int.MaxValue. The
        // inline window already searches against a bounded limit. Reopen on an observed stall.
        var perfStart = SearchPerf.Start();
        long searchRevision = 0;
        index.Read<object?>((snapshot, delta) =>
        {
            searchRevision = index.Revision;
            var directoryFilterLower = DirectoryFilterResolver.NormalizeFilter(directoryFilter);
            if (directoryFilterLower != null && directoryFilterLower.Equals(snapshot.SourceRoot.ToLowerInvariant(), StringComparison.Ordinal))
                directoryFilterLower = null;

            if (parsed.IsPathMode)
            {
                PathSearch.SearchStreaming(snapshot, delta, parsed, limit, onResult, token, directoryFilterLower,
                    FilterPatternHelper.SplitOrNullIfMatchAll(fileNameFilter), category);
                return null;
            }

            var pattern = FzfPattern.Parse(query);
            NameSearch.SearchStreaming(snapshot, delta, pattern, limit, CacheOrOriginal(), token, directoryFilterLower,
                FilterPatternHelper.SplitOrNullIfMatchAll(fileNameFilter), category);
            return null;
        });
        if (cacheKey != null)
            index.SearchPages.Store(cacheKey, captured!, limit, searchRevision, emitted);
        SearchPerf.Log($"[search] q='{query}' limit={limit} dir='{directoryFilter}' pathMode={parsed.IsPathMode} build={SearchPerf.BuildFlavor} total={SearchPerf.Ms(perfStart):F0}ms");

        Action<SearchResult> CacheOrOriginal() => cacheKey == null ? onResult : Capture;
    }

    // Directory listing rather than search (see DirectoryEnumerator): no query, no ranking, walks the
    // index's own parent->children structure. False = this drive's index doesn't hold that path.
    public static bool EnumerateDirectory(LiveIndex index, string path, bool recursive, string[]? patterns, int limit, Action<SearchResult> onResult, CancellationToken token)
        => index.Read((snapshot, delta) => DirectoryEnumerator.Enumerate(snapshot, delta, path, recursive, patterns, limit, onResult, token));

    public static void GetRecentFiles(LiveIndex index, string dirLower, uint cutoffUtc, List<SearchResult> candidates) => index.Read<object?>((snapshot, delta) =>
                                                                                                                               {
                                                                                                                                   RecentFilesV2.CollectFromDirectory(snapshot, delta, dirLower, snapshot.SourceKey, cutoffUtc, candidates);
                                                                                                                                   return null;
                                                                                                                               });
}
