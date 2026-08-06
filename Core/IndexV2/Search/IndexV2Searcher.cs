using Lertaro.Core.SearchIndex.Fzf;

using Lertaro.Core.IndexV2.Search.PathMode;
using Lertaro.Core.SearchIndex.Query;
namespace Lertaro.Core.IndexV2.Search;

// Top-level search entry point for a single drive's LiveIndex, mirroring Searcher.SearchStreaming's
// dispatch: parse the query, route path-mode queries to PathSearch and everything else to NameSearch,
// normalize/gate the directory filter the same way. Runs entirely inside one LiveIndex.Read call so
// the whole search sees one consistent (Snapshot, DeltaOverlay) pair.
public static class IndexV2Searcher
{
    public static void SearchStreaming(LiveIndex index, string query, int limit, Action<SearchResult> onResult, CancellationToken token, string? directoryFilter = null)
    {
        if (limit <= 0 || string.IsNullOrWhiteSpace(query))
            return;

        var parsed = SearchQueryParser.Parse(query);
        index.Read<object?>((snapshot, delta) =>
        {
            var directoryFilterLower = DirectoryFilterResolver.NormalizeFilter(directoryFilter);
            if (directoryFilterLower != null && directoryFilterLower.Equals(snapshot.SourceRoot.ToLowerInvariant(), StringComparison.Ordinal))
                directoryFilterLower = null;

            if (parsed.IsPathMode)
            {
                PathSearch.SearchStreaming(snapshot, delta, parsed, limit, onResult, token, directoryFilterLower);
                return null;
            }

            var pattern = FzfPattern.Parse(query);
            NameSearch.SearchStreaming(snapshot, delta, pattern, limit, onResult, token, directoryFilterLower);
            return null;
        });
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
