using System.Windows;
using Lertaro.Core;
using Lertaro.App.Services.Plugin;
using Lertaro.App.ViewModels.Service;

using Lertaro.Core.SearchIndex.Query;
using Lertaro.App.ViewModels.Search.Mapping;

using SearchWindowType = Lertaro.PluginSdk.Abstractions.SearchWindowType;
namespace Lertaro.App.ViewModels.Search.Dispatch;

// Owns query-token parsing and search dispatch for the full search window's SearchViewModel --
// extracted into its own class (composition, not a partial class) purely to keep SearchViewModel.cs
// under the repo's per-file line limit.
internal sealed class SearchQueryDispatchController
{
    private readonly SearchExecutionEngine _searchEngine;
    private readonly SearchServiceStatusViewModel _serviceStatus;
    private readonly Func<List<AppSearchResult>> _getAllResults;
    // The bool records whether the list handed in holds content-provider rows (only the list from
    // ScheduleContentRowAppend does). SearchViewModel needs it to decide whether a TYPE filter has anything
    // to exclude: the alternative is copying a list that can hold hundreds of thousands of rows, on the UI
    // thread, on every paint. Asking the list itself is not enough -- its order is not stable, a column
    // sort reorders the very rows this has to find.
    private readonly Action<List<AppSearchResult>, bool> _setAllResults;
    private readonly Action<bool> _setIsSearching;
    private readonly Action<Visibility> _setLoadingPanelVisibility;
    private readonly Action<bool> _setIsSearchBoxEnabled;
    private readonly Action<int> _setReceivedCount;
    private readonly Action<IReadOnlyList<AppSearchResult>, bool> _updateSidebarCounts;
    private readonly Action<IReadOnlyList<AppSearchResult>> _replaceSidebarCounts;
    private readonly Func<bool> _isTypeFilterSelected;
    // bool: whether this render extends what is already on screen (a later paint of a search still
    // streaming) rather than replacing it with a different result set.
    // int: index of the first row this render changed -- everything before it is already correct on
    // screen. See StreamingResultAccumulator.FirstChangedIndex.
    private readonly Action<bool, int> _applyFiltersAndRender;

    private IReadOnlyList<string> _queryTokens = Array.Empty<string>();

    // Bumped per query so an append that lands after the user typed again cannot paint stale rows.
    private int _contentAppendGeneration;

    public SearchQueryDispatchController(
        SearchExecutionEngine searchEngine,
        SearchServiceStatusViewModel serviceStatus,
        Func<List<AppSearchResult>> getAllResults,
        Action<List<AppSearchResult>, bool> setAllResults,
        Action<bool> setIsSearching,
        Action<Visibility> setLoadingPanelVisibility,
        Action<bool> setIsSearchBoxEnabled,
        Action<int> setReceivedCount,
        Action<IReadOnlyList<AppSearchResult>, bool> updateSidebarCounts,
        Action<IReadOnlyList<AppSearchResult>> replaceSidebarCounts,
        Action<bool, int> applyFiltersAndRender,
        Func<bool> isTypeFilterSelected)
    {
        _searchEngine = searchEngine;
        _serviceStatus = serviceStatus;
        _getAllResults = getAllResults;
        _setAllResults = setAllResults;
        _setIsSearching = setIsSearching;
        _setLoadingPanelVisibility = setLoadingPanelVisibility;
        _setIsSearchBoxEnabled = setIsSearchBoxEnabled;
        _setReceivedCount = setReceivedCount;
        _updateSidebarCounts = updateSidebarCounts;
        _replaceSidebarCounts = replaceSidebarCounts;
        _applyFiltersAndRender = applyFiltersAndRender;
        _isTypeFilterSelected = isTypeFilterSelected;
    }

    public void OnAdvancedQueryChanged(string query)
    {
        var globalPrefixChar = GetGlobalTokenPrefixChar();
        var strippedTrailing = SearchQuerySortParser.Strip(query, out var tokens, globalPrefixChar);
        _queryTokens = tokens;
        var cleanQuery = SearchQuerySortParser.StripExclusionBypass(strippedTrailing, out var bypassExclusions);
        // A file-filter scope keyword ("tf report" -> search "report" only inside the tf filter's folders)
        // resolves first, in the same order SearchDispatchController applies it: the scope is the more
        // specific prefix and its own resolver owns stripping its word, so the trigger-word strip below is
        // skipped when it claimed the leading token. Without this, "Show more"/Ctrl+F carrying a scoped
        // quick-window query landed here as literal text and this window searched for "tf report".
        var scopedQuery = cleanQuery;
        var scopeDirective = FileFilterScopeResolver.Resolve(cleanQuery, out scopedQuery);
        // Same rule as the quick/inline windows: a leading trigger word ("cs report" ->
        // search "report") must not be fuzzy-matched against file names or highlighted. Overwritten in
        // place so the streaming accumulator below ranks by the very term being searched. This window is
        // the full search window, so its action-word inventory is SearchWindowType.Main's.
        cleanQuery = scopeDirective != null
            ? scopedQuery
            : PluginTriggerQuery.Strip(cleanQuery, SearchWindowType.Main);

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
        StreamingResultAccumulator? accumulator = null;

        // Content-style file providers (e.g. ContentSearch's "cs " hits) answer from their own database, and
        // building the rows means one row per hit. Started here instead of during the settled render so it
        // overlaps the file search rather than extending it, and so the UI thread never waits on it: when it
        // ran inside the final render, a large content index held the one thread every window's paint,
        // status callback and cancellation runs on -- the app stopped answering input and could not even be
        // cancelled out of it.
        //
        // Not created for a file-filter scope: the scope says the folders it configures are the whole result
        // domain, so its rows would be dropped again. Nor for a query carrying a :token -- that render path
        // never merges them either. Whether a TYPE filter is active is deliberately NOT checked here: that is
        // UI-thread state, and waiting to read it is exactly what blocked the render above. It is checked
        // where the rows land instead.
        var queryGeneration = Interlocked.Increment(ref _contentAppendGeneration);
        var contentRowsTask = scopeDirective == null && _queryTokens.Count == 0
            ? Task.Run(() => BuildFullSearchFileRows(query))
            : null;

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
                // The full window is a file-browser-style view: only rank actual index matches here.
                // Quick and inline search retain their separate history/favorite learning behavior.
                accumulator ??= new StreamingResultAccumulator(cleanQuery, new Dictionary<string, int>());
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
                if (final)
                    _replaceSidebarCounts(filteredResults);
                else
                    _updateSidebarCounts(accumulator?.LastBatchRows ?? Array.Empty<AppSearchResult>(), false);
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
                    _setAllResults(snapshot, false);
                    _ = RefreshAfterTokenDispatchAsync(snapshot, _queryTokens, extendsContent);
                }
                else
                {
                    _setAllResults(filteredResults, false);
                    _applyFiltersAndRender(extendsContent, accumulator?.FirstChangedIndex ?? 0);
                    // Content rows are real files and belong in this window's grid, but only once the file
                    // search has settled -- added mid-stream they would be re-ordered away by the next paint.
                    // The fetch that produced them started with the query; this only schedules the paint for
                    // whenever it actually finishes.
                    if (final && contentRowsTask != null)
                        ScheduleContentRowAppend(contentRowsTask, queryGeneration);
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
            // it). Suppressed at the source rather than by the late shouldEmitInstantResults hook: the
            // late hook still makes every provider do the work, and this window throws all of it away --
            // its content rows come from BuildFullSearchFileRows above, which asks the same providers for
            // the file-shaped view they are worth here.
            emitInstantResults: false,
            bypassExclusions: bypassExclusions,
            resultMapperConsumesBatches: true,
            // A resolved file-filter scope: the engine runs one query per configured folder and keeps only
            // file names matching the filter's pattern, exactly as the quick window's scoped search does.
            scopeDirective: scopeDirective,
            // The untouched box text: this window's providers still have to recognise a trigger word that
            // cleanQuery above has already had stripped.
            instantQuery: query,
            onReceivedCountUpdated: count =>
            {
                if (_queryTokens.Count == 0)
                    _setReceivedCount(count);
            }
        );
    }

    /// <summary>
    /// Asks every content-style file provider for its rows. Runs on the thread pool -- see
    /// <see cref="OnAdvancedQueryChanged"/>, where the task for the current query is started.
    /// </summary>
    /// <param name="query">The RAW box text, not the stripped query: a content provider recognises its own
    /// trigger word, and the host has already taken that word out of what the file index searches. Handing
    /// it the stripped text would ask it to match a prefix that is no longer there.</param>
    private static List<AppSearchResult> BuildFullSearchFileRows(string query)
    {
        var extras = new List<AppSearchResult>();
        foreach (var provider in PluginManager.Instance.FullSearchFileResultProviders)
        {
            try
            {
                var items = PluginPerformanceMonitor.Measure(provider, () => provider.GetFileResults(query, 2000));
                PluginSearchResultMapper.AddInstantResultItems(extras, items, query, provider);
            }
            catch (Exception ex)
            {
                Logger.Log($"[SearchQueryDispatch] Full search file provider '{provider.Name}' failed: {ex.Message}", LogLevel.Error);
            }
        }

        return extras;
    }

    /// <summary>
    /// Paints <paramref name="contentRowsTask"/>'s rows ahead of the settled file results as soon as the
    /// provider finishes, so the settled render itself never waits on plugin I/O.
    /// </summary>
    /// <remarks>
    /// Once per query, on the final render only: content rows come from a different source than the index
    /// matches and are prepended, so adding them while the file search still streams would just be
    /// re-ordered away by the next paint.
    ///
    /// A selected TYPE filter drops them here -- a type filter means "exactly this type", and the extra
    /// content rows are outside that contract. That check can only be made on the UI thread, which is the
    /// whole reason this is a second paint rather than part of the first one.
    /// </remarks>
    private void ScheduleContentRowAppend(Task<List<AppSearchResult>> contentRowsTask, int generation)
    {
        _ = contentRowsTask.ContinueWith(t =>
        {
            if (generation != Volatile.Read(ref _contentAppendGeneration))
                return;

            var extras = t.Result;
            if (extras.Count == 0)
                return;

            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null)
                return;

            _ = dispatcher.InvokeAsync(() =>
            {
                // Re-checked: the user may have typed again or selected a type filter while the provider ran.
                if (generation != Volatile.Read(ref _contentAppendGeneration) || _isTypeFilterSelected())
                    return;

                var fileRows = _getAllResults();
                var merged = new List<AppSearchResult>(extras.Count + fileRows.Count);
                // Content-search hits lead the list, ahead of the regular file-index matches, per this
                // window's content-search priority rule.
                merged.AddRange(extras);
                merged.AddRange(fileRows);
                _setAllResults(merged, true);
                // Prepending changes every row's position, so no scroll anchor can survive; this is a fresh
                // result set as far as the view is concerned.
                _applyFiltersAndRender(false, 0);
            });
        }, CancellationToken.None, TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Default);
    }

    private async Task RefreshAfterTokenDispatchAsync(List<AppSearchResult> resultsSnapshot, IReadOnlyList<string> tokensSnapshot, bool extendsContent)
    {
        List<AppSearchResult> dispatched;
        try
        {
            dispatched = await QueryTokenDispatcher.ApplyAsync(resultsSnapshot, tokensSnapshot);
        }
        catch (Exception ex)
        {
            // This is awaited by nobody, so a throwing token provider used to vanish: no log carrying the
            // query, no error reaching the UI, and because the render step below never ran the window
            // kept the previous query's rows under the text the user just typed -- "search is stuck", not
            // "a plugin failed". The snapshot is already what _allResults holds, so rendering it is the
            // honest untokenized fallback.
            Logger.Log($"[SearchQueryDispatch] Query-token dispatch failed: {ex.Message}. Showing the untokenized results.", LogLevel.Warn);
            if (ReferenceEquals(_getAllResults(), resultsSnapshot) && ReferenceEquals(_queryTokens, tokensSnapshot))
                _applyFiltersAndRender(extendsContent, 0);
            return;
        }

        if (!ReferenceEquals(_getAllResults(), resultsSnapshot) || !ReferenceEquals(_queryTokens, tokensSnapshot))
            return;
        _setAllResults(dispatched, false);
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
        // Supersedes any content append still in flight, exactly as a new query does.
        Interlocked.Increment(ref _contentAppendGeneration);
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
