using Lertaro.App.Helpers;

namespace Lertaro.App.ViewModels.Search;

// Builds the synthetic rows shown by an empty inline search box. Kept separate so the dispatch
// controller only coordinates the search flow and this path-deduplication logic remains testable.
internal static class InlineEmptyStateResultHelper
{
    public static List<AppSearchResult> Build(
        AppSearchResult? recentSuggestion,
        IEnumerable<string> openedFolderPaths,
        string recentFoldersHeader,
        string openedFoldersHeader)
    {
        var results = new List<AppSearchResult>();
        if (recentSuggestion != null)
        {
            SearchResultHelper.AddSectionHeader(results, recentFoldersHeader, string.Empty);
            results.Add(recentSuggestion);
        }

        // Every folder the file manager reports as open is listed, in the order it reports them, and that
        // deliberately includes the folder the user is in right now. Nothing is filtered out here: the list
        // is what the file manager itself calls open, and its own order already puts the folder being looked
        // at (the tab Directory Opus marks active_tab) first -- dropping that one as "the folder you are
        // already in" removed exactly the entry this list gets scanned for. It can be the same folder the
        // recent-directory row above points at; the two sections answer different questions (where you were
        // vs. what is open), so both rows stay.
        var openedRows = new List<AppSearchResult>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawPath in openedFolderPaths)
        {
            var path = rawPath.Trim();
            if (path.Length == 0)
                continue;

            // Two tabs on one folder are one folder to offer, so the same folder reached by two spellings
            // (a drive root with and without its separator, say) is still one row; the row itself keeps the
            // path as reported.
            var normalizedPath = FavoritePathResolver.NormalizeForComparison(path);
            if (!seenPaths.Add(normalizedPath))
                continue;

            openedRows.Add(new AppSearchResult
            {
                Name = path,
                FullPath = path,
                ParentDir = string.Empty,
                IsDir = true,
                Drive = string.Empty,
                ResultKind = "OpenedFolder",
                SearchQuery = string.Empty
            });
        }

        if (openedRows.Count > 0)
        {
            SearchResultHelper.AddSectionHeader(results, openedFoldersHeader, string.Empty);
            results.AddRange(openedRows);
        }

        for (var index = 0; index < results.Count; index++)
            results[index].Index = index;

        return results;
    }
}
