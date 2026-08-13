using System.Windows;
using Lertaro.Core;
using Lertaro.App.ViewModels.Service;

using Lertaro.Core.SearchIndex.Query;
using Lertaro.App.ViewModels.Search.Mapping;
namespace Lertaro.App.ViewModels.Search.Dispatch;

// Owns query-token parsing and search dispatch for the full search window's SearchViewModel --
// extracted into its own class (composition, not a partial class) purely to keep SearchViewModel.cs
// under the repo's per-file line limit.
internal sealed class SearchQueryDispatchController
{
    private readonly SearchExecutionEngine _searchEngine;
    private readonly SearchServiceStatusViewModel _serviceStatus;
    private readonly Func<List<AppSearchResult>> _getAllResults;
    private readonly Action<List<AppSearchResult>> _setAllResults;
    private readonly Action<bool> _setIsSearching;
    private readonly Action<Visibility> _setLoadingPanelVisibility;
    private readonly Action<bool> _setIsSearchBoxEnabled;
    private readonly Action<int> _setReceivedCount;
    // bool: whether this render extends what is already on screen (a later paint of a search still
    // streaming) rather than replacing it with a different result set.
    // int: index of the first row this render changed -- everything before it is already correct on
    // screen. See StreamingResultAccumulator.FirstChangedIndex.
    private readonly Action<bool, int> _applyFiltersAndRender;

    private IReadOnlyList<string> _queryTokens = Array.Empty<string>();

    public SearchQueryDispatchController(
        SearchExecutionEngine searchEngine,
        SearchServiceStatusViewModel serviceStatus,
        Func<List<AppSearchResult>> getAllResults,
        Action<List<AppSearchResult>> setAllResults,
        Action<bool> setIsSearching,
        Action<Visibility> setLoadingPanelVisibility,
        Action<bool> setIsSearchBoxEnabled,
        Action<int> setReceivedCount,
        Action<bool, int> applyFiltersAndRender)
    {
        _searchEngine = searchEngine;
        _serviceStatus = serviceStatus;
        _getAllResults = getAllResults;
        _setAllResults = setAllResults;
        _setIsSearching = setIsSearching;
        _setLoadingPanelVisibility = setLoadingPanelVisibility;
        _setIsSearchBoxEnabled = setIsSearchBoxEnabled;
        _setReceivedCount = setReceivedCount;
        _applyFiltersAndRender = applyFiltersAndRender;
    }

    public void OnAdvancedQueryChanged(string query)
    {
        var globalPrefixChar = GetGlobalTokenPrefixChar();
        var strippedTrailing = SearchQuerySortParser.Strip(query, out var tokens, globalPrefixChar);
        _queryTokens = tokens;
        var cleanQuery = SearchQuerySortParser.StripExclusionBypass(strippedTrailing, out var bypassExclusions);

        if (string.IsNullOrWhiteSpace(cleanQuery))
        {
            ClearResults();
            return;
        }

        // Per-query, because this lambda chain is rebuilt on every OnAdvancedQueryChanged call: the
        // first paint of a query is a new result set, every later one is that same set growing as the
        // search streams. The view uses the distinction to decide whether the user's place in the list
        // still means anything (see ResultsControl's scroll anchor) -- without it, every 150ms repaint
        // of a multi-second search would throw them back to the top.
        var rendersSoFar = 0;

        // Also per-query: this window paints many times as a broad search streams, and rebuilding every
        // row from scratch each time is what made painting expensive enough to have to ration. The
        // accumulator maps and ranks only what arrived since the previous paint and merges it into the
        // order already established, so the total cost of painting twenty times is the cost of painting
        // once. See StreamingResultAccumulator.
        // Created by the background render callback because validating remembered paths can touch slow
        // network locations. The UI thread only schedules the search and never performs those checks.
        StreamingResultAccumulator? accumulator = null;

        _searchEngine.QueueSearch(
            cleanQuery,
            searchScope: null,
            isInlineSearchContext: false,
            fileLimit: SearchViewModel.FullSearchFileLimit,
            appLimit: SearchViewModel.FullSearchAppLimit,
            // Local (USN-indexed) and network-drive results stream in from separate, independently-timed
            // sources (see Core.Services.SearchService.SearchStreamingAsync's localTask/networkTask) and
            // land in fileResults in WHATEVER order they happened to arrive -- not relevance order.
            // SearchResultMapper.BuildQuickResults (the quick/inline windows) re-sorts by rank before
            // building rows; the accumulator does the same thing incrementally, merging each new arrival
            // into the ranking rather than redoing it.
            resultMapper: (fileResults, _) =>
            {
                if (fileResults == null)
                    return new List<AppSearchResult>();
                accumulator ??= CreateAccumulator(cleanQuery);
                return accumulator.AbsorbBatch(fileResults);
            },
            searching => _setIsSearching(searching),
            (results, status, final) =>
            {
                _serviceStatus.ClearReconnectState();
                _setLoadingPanelVisibility(Visibility.Collapsed);
                _setIsSearchBoxEnabled(true);
                // This window has its own "no results" hint (ShowNoResultsHint, keyed off an empty
                // FilteredResults) -- the shared engine's synthetic "Empty" placeholder row is meant
                // for the quick/inline windows, which have no such hint and render it inline instead.
                // Left in here, it counts toward FilteredResults.Count and shows up as a real grid row.
                // Copied only when there is genuinely something to drop. The engine appends its
                // synthetic "Empty" placeholder in exactly one case (a final render that found nothing),
                // so on every other paint this filter used to duplicate the entire row list -- megabytes
                // onto the large object heap, on the UI thread, once per paint -- to remove nothing.
                var filteredResults = results.Exists(r => r.IsEmptyResult)
                    ? results.FindAll(r => !r.IsEmptyResult)
                    : results;
                var extendsContent = rendersSoFar++ > 0;
                // Token providers (e.g. the built-in ":[SCMA]"/".ext"/"::expr" sort+filter+match
                // plugin) render via a follow-up ApplyFiltersAndRender inside
                // RefreshAfterTokenDispatchAsync instead of the call below -- a provider with no
                // genuine async work (a plain filter, no metadata fetch) resolves its
                // already-completed Task inline, so RefreshAfterTokenDispatchAsync can run to
                // completion synchronously right here; rendering the raw (pre-token) results below
                // would then immediately clobber its filtered result with the unfiltered one.
                if (_queryTokens.Count > 0)
                {
                    // Copied because this outlives the render: the accumulator hands back one buffer it
                    // reuses on the next paint, which is safe for a synchronous consumer and not for one
                    // that awaits.
                    //
                    // The SAME copy has to become _allResults. RefreshAfterTokenDispatchAsync decides
                    // whether its result is still wanted by comparing the snapshot it was handed against
                    // _allResults BY REFERENCE, so handing it a copy while _allResults kept the original
                    // made that check fail every single time and silently discard every token dispatch --
                    // tokens in this window quietly stopped doing anything at all.
                    var snapshot = new List<AppSearchResult>(filteredResults);
                    _setAllResults(snapshot);
                    _ = RefreshAfterTokenDispatchAsync(snapshot, _queryTokens, extendsContent);
                }
                else
                {
                    _setAllResults(filteredResults);
                    _applyFiltersAndRender(extendsContent, accumulator?.FirstChangedIndex ?? 0);
                }
                if (final)
                    _setIsSearching(false);
            },
            () => _serviceStatus.CheckServiceStatusOnStartup(),
            // Unlike the quick/inline windows' SearchResultMapper.BuildQuickResults, this window's own
            // resultMapper above only ever builds rows from real file matches -- it never folds instant
            // results (a pasted URL, a calculator expression, ...) into the final render at all. Left at
            // the default (emit unconditionally), SearchExecutionEngine.PerformSearch would still show
            // that instant row the moment it's typed, only for the follow-up file-search render (which
            // finds no file matches for something like a URL) to immediately wipe it back out -- a
            // flash-then-vanish row that doesn't belong in this window's file-browser-style grid anyway
            // (an "InstantResult" row has no real path/size/type, so those columns render nonsense for
            // it). Suppressing the up-front emission here means instant results simply never appear in
            // this window, matching that the settled render never included them to begin with.
            shouldEmitInstantResults: () => false,
            bypassExclusions: bypassExclusions,
            resultMapperConsumesBatches: true,
            onReceivedCountUpdated: count =>
            {
                if (_queryTokens.Count == 0)
                    _setReceivedCount(count);
            }
        );
    }

    private static StreamingResultAccumulator CreateAccumulator(string query)
    {
        var learned = HistorySearchCandidateMapper.Collect(query, null);
        var priorities = HistorySearchCandidateMapper.ApplyPriorities(SearchHistoryStore.Snapshot(), learned);
        return new StreamingResultAccumulator(query, priorities, learned);
    }

    private async Task RefreshAfterTokenDispatchAsync(List<AppSearchResult> resultsSnapshot, IReadOnlyList<string> tokensSnapshot, bool extendsContent)
    {
        var dispatched = await QueryTokenDispatcher.ApplyAsync(resultsSnapshot, tokensSnapshot);
        if (!ReferenceEquals(_getAllResults(), resultsSnapshot) || !ReferenceEquals(_queryTokens, tokensSnapshot))
            return;
        _setAllResults(dispatched);
        // A token provider may filter or reorder anything, so no prefix survives.
        _applyFiltersAndRender(extendsContent, 0);
    }

    public void PerformSearch(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            ClearResults();
            return;
        }

        OnAdvancedQueryChanged(query);
    }

    private void ClearResults()
    {
        _searchEngine.CancelPendingSearch();
        _setIsSearching(false);
        _getAllResults().Clear();
        _applyFiltersAndRender(false, 0);
        _setLoadingPanelVisibility(Visibility.Collapsed);
    }

    private static char GetGlobalTokenPrefixChar()
    {
        var prefix = UserSettings.Load().GlobalTokenPrefix;
        return !string.IsNullOrEmpty(prefix) ? prefix[0] : ':';
    }
}
