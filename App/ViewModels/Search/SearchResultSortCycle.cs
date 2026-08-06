namespace Lertaro.App.ViewModels.Search;

// Pure state-transition logic for SearchViewModel.SortByColumn's three-state column-sort cycle --
// ascending, then descending, then back to the default relevance-ranked order -- extracted so it's
// unit-testable without needing to construct the full SearchViewModel (real SearchService, PluginManager
// singletons, etc., none of it with an injectable seam).
internal static class SearchResultSortCycle
{
    public static (string Column, bool IsAscending) Advance(string currentColumn, bool isAscending, string clickedColumn)
    {
        if (currentColumn != clickedColumn)
            return (clickedColumn, true);

        return isAscending
            ? (clickedColumn, false)
            : (string.Empty, true);
    }
}
