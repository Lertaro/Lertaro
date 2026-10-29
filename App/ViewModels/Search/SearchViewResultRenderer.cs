using Lertaro.App.Helpers;
using Lertaro.Core;

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
        var perfStart = SearchPerf.Start();
        // Reconcile row-by-row so streaming paints recycle existing containers instead of resetting
        // the entire list. The unchanged-prefix promise applies only when the result list is the
        // original accumulator list and no sort or filter created a different list object.
        var unchangedPrefix = _getEffectiveUnchangedPrefix(finalResults);
        _filteredResults.ReconcileTo(finalResults, SearchResultsReconciler.ItemsEqual, _getExtendsContent(), unchangedPrefix);
        // Same reason as the streaming path's own call: ItemsEqual keeps a row whose content matches, so
        // the kept instance still holds the previous query and has to be told this one for its highlight
        // to follow along (see SearchResultsReconciler.SyncMutableDisplayState).
        SearchResultsReconciler.SyncMutableDisplayState(_filteredResults, finalResults);
        _setResultCount(finalResults.Count);
        _refreshHints();

        // How long the window needs after this paint to go idle: layout, the grid's own handling of the
        // collection change, and realization of whatever rows it decides to build. That work is not
        // inside the stopwatch above and is the one part of the path no in-process timer can attribute.
        if (SearchPerf.Enabled && System.Windows.Application.Current?.Dispatcher is { } dispatcher)
        {
            var rows = finalResults.Count;
            dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle,
                new Action(() => SearchPerf.Log($"[ui.settled] rows={rows} +{SearchPerf.Ms(perfStart):F0}ms")));
        }
    }
}
