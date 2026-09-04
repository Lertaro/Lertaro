using System.Windows;
using Lertaro.Core;
using Lertaro.App.Services;

using Lertaro.Core.SearchIndex.Query;
using Lertaro.App.ViewModels.Search.Mapping;
namespace Lertaro.App.ViewModels.Search.Dispatch;

// Owns query-token parsing, dispatching a search (debounced/quick vs. blocking), and rendering the
// resulting rows on behalf of SearchExecutionViewModel -- extracted into its own class (composition,
// not a partial class) purely to keep SearchExecutionViewModel.cs under the repo's per-file line limit.
internal sealed class SearchDispatchController
{
    private readonly SearchExecutionEngine _engine;
    private readonly QuickSearchViewModel _mainVm;
    private readonly Func<string?> _getSearchScope;
    private readonly Func<bool> _getIsInlineSearchContext;
    private readonly Func<string> _getSearchQuery;
    private readonly Action<bool> _setIsSearching;
    private readonly Action<Visibility> _setResultsPanelVisibility;
    private readonly Action<Visibility> _setResultsSeparatorVisibility;
    private readonly Action<IEnumerable<AppSearchResult>> _replaceResults;
    private readonly Func<int> _getResultsCount;
    private readonly ResultTypeTriggerHandler _resultTypeTrigger;

    private IReadOnlyList<string> _queryTokens = Array.Empty<string>();
    private bool _bypassExclusions;

    public SearchDispatchController(
        SearchExecutionEngine engine,
        QuickSearchViewModel mainVm,
        Func<string?> getSearchScope,
        Func<bool> getIsInlineSearchContext,
        Func<string> getSearchQuery,
        Action<bool> setIsSearching,
        Action<Visibility> setResultsPanelVisibility,
        Action<Visibility> setResultsSeparatorVisibility,
        Action<IEnumerable<AppSearchResult>> replaceResults,
        Func<int> getResultsCount)
    {
        _engine = engine;
        _mainVm = mainVm;
        _getSearchScope = getSearchScope;
        _getIsInlineSearchContext = getIsInlineSearchContext;
        _getSearchQuery = getSearchQuery;
        _setIsSearching = setIsSearching;
        _setResultsPanelVisibility = setResultsPanelVisibility;
        _setResultsSeparatorVisibility = setResultsSeparatorVisibility;
        _replaceResults = replaceResults;
        _getResultsCount = getResultsCount;
        _resultTypeTrigger = new ResultTypeTriggerHandler(
            getIsInlineSearchContext,
            setIsSearching,
            setResultsPanelVisibility,
            setResultsSeparatorVisibility,
            replaceResults);
    }

    public void DispatchSearch(string value)
    {
        var globalPrefixChar = GetGlobalTokenPrefixChar();
        var strippedTrailing = SearchQuerySortParser.Strip(value, out var tokens, globalPrefixChar);
        _queryTokens = tokens;
        var cleanQuery = SearchQuerySortParser.StripExclusionBypass(strippedTrailing, out var bypassExclusions);
        _bypassExclusions = bypassExclusions;
        var (strippedClean, triggeredTypeId) = _resultTypeTrigger.StripTrigger(value, cleanQuery);
        cleanQuery = strippedClean;

        // File-filter scope keyword ("tf report" -> search "report" only inside the tf filter's
        // folders): quick window only, and never stacked on a per-type trigger (an Applications-only
        // trigger over a scoped file search serves neither feature).
        var scopedQuery = cleanQuery;
        var scopeDirective = triggeredTypeId == null && !_getIsInlineSearchContext()
            ? FileFilterScopeResolver.Resolve(cleanQuery, out scopedQuery)
            : null;
        var searchQuery = scopeDirective != null ? scopedQuery : cleanQuery;

        if (string.IsNullOrWhiteSpace(cleanQuery))
        {
            _engine.CancelPendingSearch();
            if (triggeredTypeId != null)
                _resultTypeTrigger.ShowPrompt(triggeredTypeId);
            else if (string.IsNullOrWhiteSpace(value))
                PerformSearch(string.Empty);
            else
                ClearForTokenOnlyQuery();
            return;
        }

        // A scope keyword typed with no term after it ("tf ") has nothing to search against yet --
        // the same "keep typing" situation as a token-only query, and preferable to both an
        // unprompted global result set and a silent no-op.
        if (scopeDirective != null && searchQuery.Length == 0)
        {
            ClearForTokenOnlyQuery();
            return;
        }

        RunEngineSearch(_engine.QueueSearch, value, searchQuery, scopeDirective);
    }

    // An operator typed with no keyword after it yet -- a token-only query (e.g. "::foo" with no
    // keyword before it), or a bare "*" (bypass exclusion rules) -- strips down to an empty clean
    // query, but the search box itself isn't empty -- unlike a genuinely empty box, this must not
    // fall back to the startup panel/recent-files history, since there's nothing typed yet for a
    // token/bypass to filter against and showing history here would look like an unrelated,
    // unprompted result set. No engine search actually runs (there's no keyword to search for), so a
    // synthetic row has to be added here explicitly instead of the real zero-match "no results" row a
    // completed search would render (see SearchExecutionEngine's own final-empty-snapshot handling) --
    // otherwise this would show nothing at all, which reads just as wrong as showing stale history.
    // "No Search Results" would also be misleading here since no search actually ran, so this uses the
    // generic "keep typing" prompt instead (see ResultTypeTriggerHandler.ShowPrompt for the type-named
    // variant used when a per-type trigger is what's waiting on more input).
    private void ClearForTokenOnlyQuery()
    {
        _setIsSearching(false);
        _replaceResults(new[] { SearchResultMapper.CreateKeepTypingPromptResult() });
        _setResultsPanelVisibility(Visibility.Visible);
        _setResultsSeparatorVisibility(Visibility.Visible);
    }

    // DispatchSearch (debounced) and PerformSearch (blocking) both resolve to the same set of
    // search parameters -- only which SearchExecutionEngine method runs them differs.
    private void RunEngineSearch(
        Action<string, string?, bool, int, int, Func<List<SearchResult>?, string?, List<AppSearchResult>>, Action<bool>, Action<List<AppSearchResult>, string, bool>, Action?, Func<bool>?, bool, bool, Action<int>?, FileFilterScopeDirective?> engineCall,
        string originalValue,
        string searchQuery,
        FileFilterScopeDirective? scopeDirective)
    {
        // A query token (e.g. "::bzsc") filters/reorders whatever candidate set it's handed in
        // ComposeAndApplyAsync, AFTER this search already ran -- the usual 51/51 quick-window budget
        // (and BuildQuickResults' own ~50-item display cap) exists to keep every ordinary keystroke
        // cheap, but it means the token only ever sees a small, plain-filename-weighted slice of
        // candidates. A common substring query (e.g. "1080") can fill that entire slice with matches
        // that have nothing to do with the token's directory filter, so the real matches never even
        // reach the token filter -- reported as "quick window returns nothing, main window finds 84".
        // Widening the budget to match the main SearchWindow's own (already-proven-viable) limit, and
        // skipping BuildQuickResults' display cap, only costs anything on the less-common token path.
        // A file-filter scope needs the same widening: its FilterPattern (a name wildcard) is applied
        // to the streamed candidates after each per-folder search, so the in-pattern matches must
        // reach the accumulator in the first place. Apps are dropped from a scoped search entirely
        // (a folder scope says nothing about applications), so no app budget is needed at all.
        var hasTokens = _queryTokens.Count > 0;
        var hasScope = scopeDirective != null;
        var fileLimit = hasTokens || hasScope ? SearchViewModel.TokenQuickSearchFileLimit : 51;
        var appLimit = hasScope ? 0 : hasTokens ? SearchViewModel.FullSearchAppLimit : 51;

        engineCall(
            searchQuery,
            hasScope ? null : _getSearchScope(),
            _getIsInlineSearchContext(),
            fileLimit,
            appLimit,
            (resp, contextDir) => SearchResultMapper.BuildQuickResults(resp, searchQuery, hasScope ? null : _getIsInlineSearchContext() ? null : _getSearchScope(), contextDir, _getIsInlineSearchContext(), originalValue, skipDisplayCap: hasTokens || hasScope, fileFilterScope: scopeDirective),
            state => _setIsSearching(state),
            (results, status, final) => ApplySearchResults(originalValue, results, status, final),
            HandleLocalServiceUnavailable,
            () => _getResultsCount() == 0,
            _bypassExclusions,
            false,
            null,
            scopeDirective
        );
    }

    public void PerformSearch(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            _engine.CancelPendingSearch();
            _setIsSearching(false);

            // An empty box shows nothing but the one suggestion the active Explorer window earns, if it
            // earns one. It used to also raise the startup panel here, which is what that panel was:
            // the empty-query state of this window. The quick panel took over what it offered and is
            // reached by its own key, so an empty box is simply empty again.
            var suggestion = ExplorerJumpSuggestionHelper.TryBuildSuggestion(_getIsInlineSearchContext(), _getSearchScope());
            if (suggestion != null)
            {
                _replaceResults(new[] { suggestion });
                _setResultsPanelVisibility(Visibility.Visible);
                _setResultsSeparatorVisibility(Visibility.Visible);
            }
            else
            {
                _replaceResults(Array.Empty<AppSearchResult>());
                _setResultsPanelVisibility(Visibility.Collapsed);
                _setResultsSeparatorVisibility(Visibility.Collapsed);
            }

            if (_mainVm.Monitor.IsIndexReady)
            {
                _mainVm.Monitor.StatusBarVisibility = Visibility.Visible;
                _mainVm.Monitor.StatusText = string.Format(TranslationManager.Instance["Service_IndexedTemplate"], _mainVm.Monitor.GetStatusFiles(), _mainVm.Monitor.GetStatusDirs());
            }
            else
            {
                _mainVm.Monitor.StatusBarVisibility = Visibility.Collapsed;
            }
            return;
        }

        var globalPrefixChar = GetGlobalTokenPrefixChar();
        var strippedTrailing = SearchQuerySortParser.Strip(query, out var tokens, globalPrefixChar);
        _queryTokens = tokens;
        var cleanQuery = SearchQuerySortParser.StripExclusionBypass(strippedTrailing, out var bypassExclusions);
        _bypassExclusions = bypassExclusions;
        var (strippedClean, triggeredTypeId) = _resultTypeTrigger.StripTrigger(query, cleanQuery);
        cleanQuery = strippedClean;

        var scopedQuery = cleanQuery;
        var scopeDirective = triggeredTypeId == null && !_getIsInlineSearchContext()
            ? FileFilterScopeResolver.Resolve(cleanQuery, out scopedQuery)
            : null;
        var searchQuery = scopeDirective != null ? scopedQuery : cleanQuery;

        if (string.IsNullOrWhiteSpace(cleanQuery))
        {
            if (triggeredTypeId != null)
                _resultTypeTrigger.ShowPrompt(triggeredTypeId);
            else
                ClearForTokenOnlyQuery();
            return;
        }

        if (scopeDirective != null && searchQuery.Length == 0)
        {
            ClearForTokenOnlyQuery();
            return;
        }

        RunEngineSearch(_engine.PerformSearch, query, searchQuery, scopeDirective);
    }

    private void HandleLocalServiceUnavailable() => _mainVm.TriggerIndexBuild();

    private void ApplySearchResults(string query, List<AppSearchResult> uiResults, string statusText, bool final)
    {
        if (_getSearchQuery() != query)
            return;

        if (_queryTokens.Count == 0)
        {
            // No active token -- render exactly what SearchResultMapper/InlineListSearchHelper already
            // built, untouched. In particular, this preserves whatever multi-header layout the caller
            // assembled (e.g. the inline window's "Current Folder"/"Global Search" split, each with its
            // own files right under its own header) -- token mode is the only case that needs to
            // extract/re-filter/re-cap that structure, since it collapses it into a flat file list anyway.
            _replaceResults(uiResults);

            var hasResults = uiResults.Count > 0;
            _setResultsPanelVisibility(hasResults ? Visibility.Visible : Visibility.Collapsed);
            _setResultsSeparatorVisibility(hasResults ? Visibility.Visible : Visibility.Collapsed);
            _mainVm.Monitor.StatusBarVisibility = Visibility.Visible;
            _mainVm.Monitor.StatusText = statusText;
            return;
        }

        _ = ComposeAndApplyAsync(query, uiResults, _queryTokens, statusText, final);
    }

    // Token mode only: extracts the file/directory subset -- the only thing a query token is allowed to
    // see or reorder -- and everything else is pruned down to just instant results (a calculator answer,
    // etc.); applications, plugin actions, and section headers have nothing to do with what the token is
    // sorting/filtering and would just clutter a result set that's now specifically about files. Runs the
    // file/directory subset through QueryTokenDispatcher, then recomposes [instant results, token-processed
    // file rows] and caps the combined count like an ordinary quick search. QueryTokenDispatcher only
    // transforms a plain list -- deciding what any of this means for the rest of the UI (capping, "no
    // results", visibility) lives here.
    private async Task ComposeAndApplyAsync(string query, List<AppSearchResult> uiResults, IReadOnlyList<string> tokensSnapshot, string statusText, bool final)
    {
        var fileRows = uiResults.Where(IsFileOrDirectory).ToList();
        // ResultKind == "InstantResult" alone isn't enough: ISearchableItemProvider (a static catalog --
        // System Settings shortcuts, Start Menu apps that don't resolve to a real file, etc., see
        // SearchableItemMapper) also defaults an item's ResultKind to "InstantResult" when it isn't a
        // File/Directory/Application, per the SDK's own documented default. A catalog shortcut has
        // nothing to do with a query token's file filter either, so only rows from a genuine
        // IInstantResultProvider (a per-query computed answer, e.g. a calculator result) survive here.
        var instantRows = uiResults.Where(IsGenuineInstantResult).ToList();

        var processedFileRows = await QueryTokenDispatcher.ApplyAsync(fileRows, tokensSnapshot);
        if (_getSearchQuery() != query || !ReferenceEquals(_queryTokens, tokensSnapshot))
            return; // superseded by a newer query/token set while the token chain was running

        var composed = QueryTokenResultComposer.Compose(instantRows, processedFileRows, query);

        // A filter token (or an unclaimed one) can legitimately drop every file/directory result -- this
        // window has no separate "no results" hint of its own (unlike the full search window), it
        // renders the synthetic "Empty" row inline. Only on the final snapshot, though -- an empty
        // intermediate streaming update just means results haven't arrived yet, not that there are none.
        if (composed.Count == 0 && final)
            composed.Add(SearchResultMapper.CreateNoResultsResult(query));

        // ReplaceResults reconciles row-by-row and no-ops when nothing changed, so no pre-check needed.
        _replaceResults(composed);

        var hasResults = composed.Count > 0;
        _setResultsPanelVisibility(hasResults ? Visibility.Visible : Visibility.Collapsed);
        _setResultsSeparatorVisibility(hasResults ? Visibility.Visible : Visibility.Collapsed);
        _mainVm.Monitor.StatusBarVisibility = Visibility.Visible;
        _mainVm.Monitor.StatusText = statusText;
    }

    private static bool IsFileOrDirectory(AppSearchResult r) => r.ResultKind is "File" or "Directory";

    private static bool IsGenuineInstantResult(AppSearchResult r) =>
        r.ResultKind == "InstantResult" && r.SourceProvider is PluginSdk.Abstractions.Plugins.IInstantResultProvider;

    private static char GetGlobalTokenPrefixChar()
    {
        var prefix = UserSettings.Load().GlobalTokenPrefix;
        return !string.IsNullOrEmpty(prefix) ? prefix[0] : ':';
    }
}
