using Lertaro.Core;
using Lertaro.Core.SearchIndex.Query;

namespace Lertaro.Cli.Search;

internal static class NonInteractiveSearchResults
{
    public static List<SearchResult> Prepare(
        IEnumerable<SearchResult> source,
        string query,
        NonInteractiveSearchOptions options)
    {
        var results = source.ToList();
        var configuredPrefix = UserSettings.Load().GlobalTokenPrefix;
        var prefix = !string.IsNullOrEmpty(configuredPrefix) ? configuredPrefix[0] : ':';
        SearchQuerySortParser.Strip(query, out var tokens, prefix);
        if (tokens.Count == 0)
            results.Sort(new SearchResultRankComparer(SearchHistoryStore.Snapshot()));

        if (options.FilesOnly)
            results.RemoveAll(result => result.IsDir);
        else if (options.FoldersOnly)
            results.RemoveAll(result => !result.IsDir);

        if (results.Count > options.Limit)
            results.RemoveRange(options.Limit, results.Count - options.Limit);
        return results;
    }
}
