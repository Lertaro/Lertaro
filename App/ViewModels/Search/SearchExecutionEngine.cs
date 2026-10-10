using Lertaro.Core;
using Lertaro.Core.SearchIndex;
using Lertaro.App.Services;
using Lertaro.Core.Services.Search;
using Lertaro.App.ViewModels.Search.Dispatch;

using Lertaro.App.ViewModels.Search.Mapping;
using System.Windows.Threading;

namespace Lertaro.App.ViewModels.Search;

internal sealed class SearchExecutionEngine : IDisposable
{
    private readonly SearchService _searchService;
    private readonly SearchStreamRenderer _streamRenderer;
    // Per-engine, and the engine lives exactly as long as its window -- see DirectChildrenListingCache.
    private readonly DirectChildrenListingCache _directChildrenListing = new();
    private readonly object _searchCtsLock = new();
    private CancellationTokenSource? _searchCts;
    private CancellationTokenSource? _debounceCts;
    private int _searchVersion;

    public SearchExecutionEngine(SearchService searchService)
    {
        _searchService = searchService;
        _streamRenderer = new SearchStreamRenderer(searchService, () => Volatile.Read(ref _searchVersion));
    }

    public void QueueSearch(
        string query,
        string? searchScope,
        bool isInlineSearchContext,
        int fileLimit,
        int appLimit,
        Func<List<SearchResult>?, string?, List<AppSearchResult>> resultMapper,
        Action<bool> onSearchStateChanged,
        Action<List<AppSearchResult>, string, bool> onResultsUpdated,
        Action? onLocalServiceUnavailable = null,
        Func<bool>? shouldEmitInstantResults = null,
        bool bypassExclusions = false,
        bool resultMapperConsumesBatches = false,
        Action<int>? onReceivedCountUpdated = null,
        FileFilterScopeDirective? scopeDirective = null,
        // The untouched box text, handed to the instant-result providers instead of `query`: the host has
        // already stripped a plugin's own trigger word out of `query` so it is not fuzzy-matched against
        // file names, but the provider that owns that word must still recognise it. Null (every other
        // caller) means "nothing was stripped", which is the same query for both purposes.
        string? instantQuery = null,
        // False for a window that can never show an instant row. `shouldEmitInstantResults` below is a
        // LATE check -- it runs after every provider has already been asked -- so leaving it at its
        // default made the full search window pay a full provider pass per keystroke and throw the
        // answer away. This is the early check the late one could not be.
        bool emitInstantResults = true,
        // Invoked on the UI thread at the moment the search itself is issued -- so after the debounce,
        // once per settled query rather than once per keystroke. For work that must overlap the search
        // but must not be repeated for characters the user typed and then replaced.
        Action? beforeSearch = null,
        Func<string?, List<AppSearchResult>>? collectInstantResults = null)
    {
        // The previous query is obsolete as soon as input changes, including while the new one
        // debounces. Otherwise its rows keep repainting ahead of the next keystroke.
        CancelPendingSearch();

        var delay = string.IsNullOrEmpty(query) || query.Length <= 1 ? 0 : (fileLimit > 100 ? 150 : 30);
        if (delay == 0)
        {
            PerformSearch(query, searchScope, isInlineSearchContext, fileLimit, appLimit, resultMapper, onSearchStateChanged, onResultsUpdated, onLocalServiceUnavailable, shouldEmitInstantResults, bypassExclusions, resultMapperConsumesBatches, onReceivedCountUpdated, scopeDirective, instantQuery, emitInstantResults, beforeSearch, collectInstantResults);
            return;
        }

        var cts = new CancellationTokenSource();
        _debounceCts = cts;
        var token = cts.Token;
        _ = RunDebouncedSearchAsync(Task.Delay(delay, token), System.Windows.Application.Current.Dispatcher,
            () => PerformSearch(query, searchScope, isInlineSearchContext, fileLimit, appLimit, resultMapper, onSearchStateChanged, onResultsUpdated, onLocalServiceUnavailable, shouldEmitInstantResults, bypassExclusions, resultMapperConsumesBatches, onReceivedCountUpdated, scopeDirective, instantQuery, emitInstantResults, beforeSearch, collectInstantResults), token);
    }

    internal static async Task RunDebouncedSearchAsync(Task delay, Dispatcher dispatcher, Action search, CancellationToken token)
    {
        try
        {
            await delay.ConfigureAwait(false);
            // Cancellation must cover the dispatcher queue as well as the delay: input can change
            // after the delay completes but before this callback gets a turn on the UI thread.
            await dispatcher.InvokeAsync(() =>
            {
                if (!token.IsCancellationRequested)
                    search();
            }, DispatcherPriority.Background, token).Task.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            Logger.Log($"[SearchExecutionEngine] Debounced search failed: {ex}", LogLevel.Error);
        }
    }

    public void PerformSearch(
        string query,
        string? searchScope,
        bool isInlineSearchContext,
        int fileLimit,
        int appLimit,
        Func<List<SearchResult>?, string?, List<AppSearchResult>> resultMapper,
        Action<bool> onSearchStateChanged,
        Action<List<AppSearchResult>, string, bool> onResultsUpdated,
        Action? onLocalServiceUnavailable = null,
        Func<bool>? shouldEmitInstantResults = null,
        bool bypassExclusions = false,
        bool resultMapperConsumesBatches = false,
        Action<int>? onReceivedCountUpdated = null,
        FileFilterScopeDirective? scopeDirective = null,
        // The untouched box text, handed to the instant-result providers instead of `query`: the host has
        // already stripped a plugin's own trigger word out of `query` so it is not fuzzy-matched against
        // file names, but the provider that owns that word must still recognise it. Null (every other
        // caller) means "nothing was stripped", which is the same query for both purposes.
        string? instantQuery = null,
        // See QueueSearch: false skips the provider pass entirely instead of running it and
        // discarding what comes back.
        bool emitInstantResults = true,
        // Invoked here, on the UI thread, as this search starts. Every search funnels through this method,
        // whether it was issued directly or waited out QueueSearch's keystroke debounce, so a caller with
        // work that should overlap the search -- and must not be repeated for every character typed --
        // hangs it here rather than reimplementing the delay.
        Action? beforeSearch = null,
        Func<string?, List<AppSearchResult>>? collectInstantResults = null)
    {
        Logger.Log($"[SearchExecutionEngine] Performing search: '{query}', scope: '{searchScope}'", LogLevel.Debug);
        CancelPendingSearch();
        if (string.IsNullOrWhiteSpace(query))
        {
            onSearchStateChanged(false);
            onResultsUpdated(new List<AppSearchResult>(), string.Empty, true);
            return;
        }

        beforeSearch?.Invoke();
        onSearchStateChanged(true);
        var cts = new CancellationTokenSource();
        var searchVersion = Interlocked.Increment(ref _searchVersion);
        lock (_searchCtsLock)
        {
            _searchCts = cts;
        }

        var token = cts.Token;
        var sourceTracker = InlineSearchManager.Instance.ExplorerTracker;
        var commandDirectory = isInlineSearchContext
            ? (!string.IsNullOrWhiteSpace(searchScope) ? searchScope : sourceTracker.ActivePath ?? sourceTracker.LastActiveExplorerPath)
            : sourceTracker.LastActiveExplorerPath;
        // Providers are invoked with the box text as typed (the owner of a trigger word has to keep
        // recognising it), but their rows are highlighted against `query` -- what the file search beside
        // them was matched with, with that word (and any :token suffix) already taken off. Same split
        // BuildQuickResults makes, so a row painted from either path highlights identically.
        if (emitInstantResults)
            EmitInstantResults(instantQuery ?? query, query, isInlineSearchContext, searchVersion, token, onResultsUpdated, shouldEmitInstantResults, commandDirectory, collectInstantResults);
        _ = Task.Run(async () =>
        {
            try
            {
                var tracker = InlineSearchManager.Instance.ExplorerTracker;
                var dialogAdapter = tracker.ActiveAdapter;
                // Scoped to what the dialog's own target field can hold, not to "is a dialog": a Browse-For-Folder
                // picker takes only a folder, an Open/Save dialog's name box takes a file too, and there the card
                // has to keep offering files. Typed into an Explorer window this card is that window's search.
                var folderScope = isInlineSearchContext && dialogAdapter?.TargetIsFolderOnly == true;
                if (isInlineSearchContext && tracker.ActiveHwnd != IntPtr.Zero
                    && (tracker.IsActiveWindowExplorer || (tracker.IsActiveWindowDialog && dialogAdapter != null)))
                {
                    var contextDirectory = commandDirectory;
                    if (!string.IsNullOrEmpty(contextDirectory))
                    {
                        await RenderInlineSearchAsync(query, contextDirectory, fileLimit, appLimit, resultMapper, searchVersion, onResultsUpdated, token, onLocalServiceUnavailable, bypassExclusions, folderScope).ConfigureAwait(false);
                        return;
                    }
                }

                var streamingScope = tracker.IsActiveWindowExplorer ? searchScope : null;
                var streamingContextDirectory = commandDirectory;
                await _streamRenderer.RenderAsync(query, streamingScope, streamingContextDirectory, fileLimit, appLimit, resultMapper, searchVersion, onResultsUpdated, token, onLocalServiceUnavailable: onLocalServiceUnavailable, bypassExclusions: bypassExclusions, resultMapperConsumesBatches: resultMapperConsumesBatches, onReceivedCountUpdated: onReceivedCountUpdated, scopeDirective: scopeDirective, foldersOnly: folderScope).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Logger.Log($"[SearchExecutionEngine] PerformSearch failed: {ex}", LogLevel.Error);
            }
            finally
            {
                _ = System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    lock (_searchCtsLock)
                    {
                        if (_searchCts == cts)
                            onSearchStateChanged(false);
                    }
                }));
            }
        }, token);
    }

    /// <summary>
    /// Starts reading the given folder's one-level listing now, so a later inline search finds it warm.
    /// </summary>
    /// <remarks>
    /// Called when the window learns its scope -- navigating Explorer, activating a file dialog -- which is
    /// before the user has typed. The walk it starts is the part that scales with the folder's size, and
    /// doing it here is what stops the first keystroke from paying for it.
    /// </remarks>
    public void PrewarmDirectoryListing(string? directory) => _directChildrenListing.Prewarm(directory);

    public void CancelPendingSearch()
    {        try
        {
            _debounceCts?.Cancel();
            _debounceCts?.Dispose();
            _debounceCts = null;
        }
        catch
        {
        }

        lock (_searchCtsLock)
        {
            _searchCts?.Cancel();
            _searchCts?.Dispose();
            _searchCts = null;
        }
    }

    public void Dispose()
    {
        CancelPendingSearch();
        _debounceCts?.Dispose();
    }

    private async Task RenderInlineSearchAsync(
        string query,
        string contextDirectory,
        int fileLimit,
        int appLimit,
        Func<List<SearchResult>?, string?, List<AppSearchResult>> resultMapper,
        int searchVersion,
        Action<List<AppSearchResult>, string, bool> onResultsUpdated,
        CancellationToken token,
        Action? onLocalServiceUnavailable,
        bool bypassExclusions,
        bool folderScope)
    {
        var localMatches = new List<AppSearchResult>();
        // The engine-side folder filter never sees these rows: history remembers files too, so the scope has
        // to be applied here as well.
        var learned = HistorySearchCandidateMapper.Collect(FuzzyQuery.Parse(query), contextDirectory);
        var learnedLocalMatches = folderScope ? learned.Where(c => c.Result.IsDir).ToList() : learned;
        var localUpdateVersion = learnedLocalMatches.Count > 0 ? 1 : 0;
        void OnLocalMatchesChanged() => Interlocked.Increment(ref localUpdateVersion);

        // The one thing the inline window does beyond the ordinary global search: list the window folder's
        // own files, so they are guaranteed present even when the global result budget is spent elsewhere.
        // Cheap and cached -- see ExplorerSearchHelper.LoadDirectChildrenAsync. There is deliberately no
        // gate before the global search below: the global search IS the result list now, and holding it
        // back behind this listing is what made the inline window lag the quick window.
        var localSearchTask = ExplorerSearchHelper.LoadDirectChildrenAsync(
            query, fileLimit, contextDirectory, localMatches, token, OnLocalMatchesChanged, _directChildrenListing, folderScope);

        // Memoized: CreateLocalSnapshot copies the whole history-priority dictionary and re-ranks every
        // local match, and the renderer asks for this snapshot on EVERY paint -- of which one keystroke can
        // produce several as results stream in. Nothing about the answer changes unless the local matches
        // changed (localUpdateVersion) or the query did, so rebuilding it per paint was repeated work that
        // grew with the user's history size.
        List<AppSearchResult>? cachedLocalSnapshot = null;
        var cachedLocalVersion = -1;

        List<AppSearchResult> GetLocalSnapshot()
        {
            var version = Volatile.Read(ref localUpdateVersion);
            if (cachedLocalSnapshot != null && cachedLocalVersion == version)
                return cachedLocalSnapshot;

            List<AppSearchResult> snapshot;
            lock (localMatches)
            {
                snapshot = new List<AppSearchResult>(localMatches);
            }

            var built = ExplorerSearchHelper.CreateLocalSnapshot(snapshot, learnedLocalMatches, query, contextDirectory);
            cachedLocalSnapshot = built;
            cachedLocalVersion = version;
            return built;
        }

        await _streamRenderer.RenderAsync(query, null, contextDirectory, fileLimit, appLimit, resultMapper, searchVersion, onResultsUpdated, token,
            GetLocalSnapshot, () => Volatile.Read(ref localUpdateVersion), localSearchTask, onLocalServiceUnavailable, bypassExclusions,
            foldersOnly: folderScope).ConfigureAwait(false);
    }

    private void EmitInstantResults(
        string query,
        string highlightQuery,
        bool isInlineSearchContext,
        int searchVersion,
        CancellationToken token,
        Action<List<AppSearchResult>, string, bool> onResultsUpdated,
        Func<bool>? shouldEmitInstantResults, string? contextDirectory,
        Func<string?, List<AppSearchResult>>? collectInstantResults) => _ = Task.Run(() =>
                                                      {
                                                          var instantResults = collectInstantResults?.Invoke(contextDirectory) ?? new List<AppSearchResult>();
                                                          if (collectInstantResults == null)
                                                              PluginSearchResultMapper.AddInstantResults(instantResults, query, highlightQuery, isInlineSearchContext, contextDirectory);
                                                          if (instantResults.Count == 0 || token.IsCancellationRequested)
                                                              return;

                                                          _ = System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                                                          {
                                                              if (token.IsCancellationRequested || searchVersion != Volatile.Read(ref _searchVersion))
                                                                  return;
                                                              if (shouldEmitInstantResults?.Invoke() ?? true)
                                                                  onResultsUpdated(instantResults, string.Empty, false);
                                                          }), DispatcherPriority.Background);
                                                      }, token);
}
