using Lertaro.App.Helpers;

namespace Lertaro.App.ViewModels.Search;

// Reconciles a fresh result list into the live ObservableRangeCollection row-by-row instead of a full
// Clear+Add reset (only changed rows are replaced in place, recycling ListBox containers, so the list
// is never torn down and rebuilt from the top -- which is what caused the flicker this was written to
// fix), and preserves the current selection when it survives the update.
internal static class SearchResultsReconciler
{
    public static void Replace(
        ObservableRangeCollection<AppSearchResult> results,
        IEnumerable<AppSearchResult> newResults,
        AppSearchResult? currentSelection,
        Action<AppSearchResult?> setSelection)
    {
        var list = newResults as List<AppSearchResult> ?? new List<AppSearchResult>(newResults);
        results.ReconcileTo(list, ItemsEqual);

        // Only re-select when the current one is gone or no longer selectable, so streaming updates
        // don't yank the highlight back to the top.
        if (currentSelection != null && results.Contains(currentSelection)
            && !currentSelection.IsEmptyResult && !currentSelection.IsSearchSectionHeader)
            return;

        AppSearchResult? firstSelectable = null;
        foreach (var result in list)
        {
            if (!result.IsEmptyResult && !result.IsSearchSectionHeader)
            {
                firstSelectable = result;
                break;
            }
        }

        setSelection(firstSelectable);
    }

    // Internal (not private) so SearchViewModel.RenderFinal can reconcile FilteredResults with this
    // exact same row-identity check, instead of maintaining a second definition that could drift.
    internal static bool ItemsEqual(AppSearchResult a, AppSearchResult b) =>
        string.Equals(a.FullPath, b.FullPath, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(a.Name, b.Name, StringComparison.Ordinal) &&
        string.Equals(a.ResultKind, b.ResultKind, StringComparison.Ordinal) &&
        string.Equals(a.SearchQuery, b.SearchQuery, StringComparison.Ordinal);
}
