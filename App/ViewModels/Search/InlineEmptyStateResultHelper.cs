using Lertaro.App.Helpers;

namespace Lertaro.App.ViewModels.Search;

// Builds the synthetic rows shown by an empty inline search box. Kept separate so the dispatch
// controller only coordinates the search flow and this path-deduplication logic remains testable.
internal static class InlineEmptyStateResultHelper
{
    public static List<AppSearchResult> Build(
        AppSearchResult? recentSuggestion,
        string? currentScope,
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

        var excludedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddExcludedPath(excludedPaths, recentSuggestion?.FullPath);
        AddExcludedPath(excludedPaths, currentScope);

        var openedRows = new List<AppSearchResult>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawPath in openedFolderPaths)
        {
            var path = rawPath.Trim();
            if (path.Length == 0)
                continue;

            var normalizedPath = FavoritePathResolver.NormalizeForComparison(path);
            if (excludedPaths.Contains(normalizedPath) || !seenPaths.Add(normalizedPath))
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

    private static void AddExcludedPath(HashSet<string> excludedPaths, string? path)
    {
        if (!string.IsNullOrWhiteSpace(path))
            excludedPaths.Add(FavoritePathResolver.NormalizeForComparison(path));
    }
}
