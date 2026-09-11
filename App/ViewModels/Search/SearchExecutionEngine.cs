using Lertaro.Core;
using Lertaro.Core.SearchIndex;
using Lertaro.App.Services;
using Lertaro.Core.Services.Search;
using Lertaro.App.ViewModels.Search.Dispatch;

using Lertaro.App.ViewModels.Search.Mapping;

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
        FileFilterScopeDirective? scopeDirective = null)
    {
        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
        var cts = new CancellationTokenSource();
        _debounceCts = cts;

        var delay = string.IsNullOrEmpty(query) || query.Length <= 1 ? 0 : (fileLimit > 100 ? 150 : 30);
        if (delay == 0)
        {
            PerformSearch(query, searchScope, isInlineSearchContext, fileLimit, appLimit, resultMapper, onSearchStateChanged, onResultsUpdated, onLocalServiceUnavailable, shouldEmitInstantResults, bypassExclusions, resultMapperConsumesBatches, onReceivedCountUpdated, scopeDirective);
            return;
        }

        _ = Task.Delay(delay, cts.Token).ContinueWith(t =>
        {
            if (t.IsCanceled)
                return;
            _ = System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                PerformSearch(query, searchScope, isInlineSearchContext, fileLimit, appLimit, resultMapper, onSearchStateChanged, onResultsUpdated, onLocalServiceUnavailable, shouldEmitInstantResults, bypassExclusions, resultMapperConsumesBatches, onReceivedCountUpdated, scopeDirective)));
        }, cts.Token);
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
        FileFilterScopeDirective? scopeDirective = null)
    {
        Logger.Log($"[SearchExecutionEngine] Performing search: '{query}', scope: '{searchScope}'", LogLevel.Debug);
        CancelPendingSearch();
        if (string.IsNullOrWhiteSpace(query))
        {
            onSearchStateChanged(false);
            onResultsUpdated(new List<AppSearchResult>(), string.Empty, true);
            return;
        }

        onSearchStateChanged(true);
        var cts = new CancellationTokenSource();
        var searchVersion = Interlocked.Increment(ref _searchVersion);
        lock (_searchCtsLock)
        {
            _searchCts = cts;
        }

        var token = cts.Token;
        EmitInstantResults(query, isInlineSearchContext, searchVersion, token, onResultsUpdated, shouldEmitInstantResults);
        _ = Task.Run(async () =>
        {
            try
            {
                var tracker = InlineSearchManager.Instance.ExplorerTracker;
                var dialogAdapter = tracker.ActiveAdapter;
                if (isInlineSearchContext && tracker.ActiveHwnd != IntPtr.Zero
                    && (tracker.IsActiveWindowExplorer || (tracker.IsActiveWindowDialog && dialogAdapter != null)))
                {
                    var contextDirectory = !string.IsNullOrWhiteSpace(searchScope)
                        ? searchScope
                        : tracker.ActivePath ?? tracker.LastActiveExplorerPath;
                    if (!string.IsNullOrEmpty(contextDirectory))
                    {
                        await RenderInlineSearchAsync(query, contextDirectory, fileLimit, appLimit, resultMapper, searchVersion, onResultsUpdated, token, onLocalServiceUnavailable, bypassExclusions).ConfigureAwait(false);
                        return;
                    }
                }

                var streamingScope = tracker.IsActiveWindowExplorer ? searchScope : null;
                var streamingContextDirectory = isInlineSearchContext
                    ? (!string.IsNullOrWhiteSpace(searchScope) ? searchScope : tracker.ActivePath ?? tracker.LastActiveExplorerPath)
                    : tracker.LastActiveExplorerPath;
                await _streamRenderer.RenderAsync(query, streamingScope, streamingContextDirectory, fileLimit, appLimit, resultMapper, searchVersion, onResultsUpdated, token, onLocalServiceUnavailable: onLocalServiceUnavailable, bypassExclusions: bypassExclusions, resultMapperConsumesBatches: resultMapperConsumesBatches, onReceivedCountUpdated: onReceivedCountUpdated, scopeDirective: scopeDirective).ConfigureAwait(false);
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
        bool bypassExclusions)
    {
        var localMatches = new List<AppSearchResult>();
        var learnedLocalMatches = HistorySearchCandidateMapper.Collect(FuzzyQuery.Parse(query), contextDirectory);
        var localUpdateVersion = learnedLocalMatches.Count > 0 ? 1 : 0;
        void OnLocalMatchesChanged() => Interlocked.Increment(ref localUpdateVersion);

        // The one thing the inline window does beyond the ordinary global search: list the window folder's
        // own files, so they are guaranteed present even when the global result budget is spent elsewhere.
        // Cheap and cached -- see ExplorerSearchHelper.LoadDirectChildrenAsync. There is deliberately no
        // gate before the global search below: the global search IS the result list now, and holding it
        // back behind this listing is what made the inline window lag the quick window.
        var localSearchTask = ExplorerSearchHelper.LoadDirectChildrenAsync(
            query, fileLimit, contextDirectory, localMatches, token, OnLocalMatchesChanged, _directChildrenListing);

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
            GetLocalSnapshot, () => Volatile.Read(ref localUpdateVersion), localSearchTask, onLocalServiceUnavailable, bypassExclusions).ConfigureAwait(false);
    }

    private void EmitInstantResults(
        string query,
        bool isInlineSearchContext,
        int searchVersion,
        CancellationToken token,
        Action<List<AppSearchResult>, string, bool> onResultsUpdated,
        Func<bool>? shouldEmitInstantResults) => _ = Task.Run(() =>
                                                      {
                                                          var instantResults = new List<AppSearchResult>();
                                                          PluginSearchResultMapper.AddInstantResults(instantResults, query, null, isInlineSearchContext);
                                                          if (instantResults.Count == 0 || token.IsCancellationRequested)
                                                              return;

                                                          _ = System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                                                          {
                                                              if (token.IsCancellationRequested || searchVersion != Volatile.Read(ref _searchVersion))
                                                                  return;
                                                              if (shouldEmitInstantResults?.Invoke() ?? true)
                                                                  onResultsUpdated(instantResults, string.Empty, false);
                                                          }));
                                                      }, token);
}
