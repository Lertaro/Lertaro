using Lertaro.App.Helpers;

namespace Lertaro.App.ViewModels.Search;

// Split out solely to keep SearchViewModel under the repository's per-file line limit. This class
// owns only the final collection reconciliation and its related notifications; query orchestration
// remains in SearchViewModel.
internal sealed class SearchViewResultRenderer
{
    private readonly ObservableRangeCollection<AppSearchResult> _filteredResults;
    private readonly Func<bool> _getExtendsContent;
    private readonly Func<List<AppSearchResult>, int> _getEffectiveUnchangedPrefix;
    private readonly Action<int> _setResultCount;
    private readonly Action _refreshHints;

    public SearchViewResultRenderer(
        ObservableRangeCollection<AppSearchResult> filteredResults,
        Func<bool> getExtendsContent,
        Func<List<AppSearchResult>, int> getEffectiveUnchangedPrefix,
        Action<int> setResultCount,
        Action refreshHints)
    {
        _filteredResults = filteredResults;
        _getExtendsContent = getExtendsContent;
        _getEffectiveUnchangedPrefix = getEffectiveUnchangedPrefix;
        _setResultCount = setResultCount;
        _refreshHints = refreshHints;
    }

    public void Render(List<AppSearchResult> finalResults)
    {
        // Reconcile row-by-row so streaming paints recycle existing containers instead of resetting
        // the entire list. The unchanged-prefix promise applies only when the result list is the
        // original accumulator list and no sort or filter created a different list object.
        var unchangedPrefix = _getEffectiveUnchangedPrefix(finalResults);
        _filteredResults.ReconcileTo(finalResults, SearchResultsReconciler.ItemsEqual, _getExtendsContent(), unchangedPrefix);
        _setResultCount(finalResults.Count);
        _refreshHints();
    }
}
