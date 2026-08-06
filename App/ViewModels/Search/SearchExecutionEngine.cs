using Lertaro.Core;
using Lertaro.App.Services;
using Lertaro.Core.Services.Search;

using Lertaro.App.ViewModels.Search.Mapping;
namespace Lertaro.App.ViewModels.Search;

internal sealed class SearchExecutionEngine : IDisposable
{
    // How long to let results accumulate before the first paint. Short enough to feel immediate, long
    // enough that a search resolving in a few milliseconds paints once (already complete) rather than
    // twice.
    private const int FirstRenderDelayMs = 40;

    // Cadence of every paint after the first, while results are still arriving.
    private const int ProgressiveRenderIntervalMs = 150;

    // Cadence once the stream has ended and the pump is working through what it hasn't painted yet.
    // Only there to leave the UI thread room between paints -- each of those ticks does a full bite's
    // worth of real work anyway, which is the real pacing.
    private const int DrainRenderIntervalMs = 25;

    private readonly SearchService _searchService;
    private CancellationTokenSource? _searchCts;
    private readonly object _searchCtsLock = new();
    private CancellationTokenSource? _debounceCts;
    private int _searchVersion;

    public SearchExecutionEngine(SearchService searchService) => _searchService = searchService;

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
        bool bypassExclusions = false)
    {
        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
        var cts = new CancellationTokenSource();
        _debounceCts = cts;

        var delay = string.IsNullOrEmpty(query) || query.Length <= 1 ? 0 : (fileLimit > 100 ? 150 : 30);
        if (delay == 0)
        {
            PerformSearch(query, searchScope, isInlineSearchContext, fileLimit, appLimit, resultMapper, onSearchStateChanged, onResultsUpdated, onLocalServiceUnavailable, shouldEmitInstantResults, bypassExclusions);
        }
        else
        {
            _ = Task.Delay(delay, cts.Token).ContinueWith(t =>
            {
                if (t.IsCanceled) return;
                _ = System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    PerformSearch(query, searchScope, isInlineSearchContext, fileLimit, appLimit, resultMapper, onSearchStateChanged, onResultsUpdated, onLocalServiceUnavailable, shouldEmitInstantResults, bypassExclusions)));
            }, cts.Token);
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
        bool bypassExclusions = false)
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

        // Show instant-provider results (web URL, calculator, env vars, …) ahead of the file search
        // rather than waiting for it to stream in -- a query like a pasted URL may match no files at
        // all, so the streaming render never fires until the whole search finishes.
        //
        // Off the UI thread, even though this used to run inline here because the providers were
        // assumed cheap and synchronous. They are neither, necessarily: a provider is third-party code,
        // and PluginSearchResultMapper then probes whatever path it hands back with File.Exists /
        // Directory.Exists and asks the shell for its icon. Any of those blocks for the SMB timeout --
        // tens of seconds -- when the path is a UNC or a mapped drive whose server is gone, which froze
        // the window mid-keystroke and then released it on its own once the timeout expired. The same
        // call already runs on a background thread through BuildQuickResults on every streaming render,
        // so this only makes the two paths agree.
        _ = Task.Run(() =>
        {
            var instantResults = new List<AppSearchResult>();
            PluginSearchResultMapper.AddInstantResults(instantResults, query, null, isInlineSearchContext);
            if (instantResults.Count == 0 || token.IsCancellationRequested)
                return;

            _ = System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                // Re-checked here rather than only above: this render is now asynchronous, so a newer
                // keystroke's results can already be on screen by the time it arrives, and painting
                // this stale instant-only snapshot over them would collapse them away.
                if (token.IsCancellationRequested || searchVersion != Volatile.Read(ref _searchVersion))
                    return;

                // Emit up-front only when the caller opts in -- the quick window allows this only while
                // its list is empty. During continuous typing the list already has rows, and an
                // instant-only snapshot would collapse the existing file rows away and then re-expand
                // them on the next (file) render -- that's the flicker. When skipped, the upcoming file
                // render still includes the instant results, so nothing is lost, just no separate
                // collapsing frame. Evaluated on the UI thread because it reads that list's state.
                if (shouldEmitInstantResults?.Invoke() ?? true)
                    onResultsUpdated(instantResults, string.Empty, false);
            }));
        }, token);

        _ = Task.Run(async () =>
        {
            try
            {
                token.ThrowIfCancellationRequested();
                var tracker = InlineSearchManager.Instance.ExplorerTracker;
                var dialogAdapter = tracker.ActiveAdapter;
                if (isInlineSearchContext && tracker.ActiveHwnd != IntPtr.Zero)
                {
                    var contextDirectory = !string.IsNullOrWhiteSpace(searchScope) ? searchScope : (tracker.ActivePath ?? tracker.LastActiveExplorerPath);
                    if (tracker.IsActiveWindowExplorer || (tracker.IsActiveWindowDialog && dialogAdapter != null))
                    {
                        var localMatches = new List<AppSearchResult>();
                        Task? localSearchTask = null;
                        if (!string.IsNullOrEmpty(contextDirectory))
                        {
                            // Always bypass ExcludedPaths/glob/regex filtering for the "current folder"
                            // section -- the user is explicitly looking at contextDirectory in Explorer
                            // right now, so global exclusion settings (meant to keep noise out of broad,
                            // untargeted searches) have no business hiding results from the one folder
                            // they're actually standing in.
                            localSearchTask = ExplorerSearchHelper.SearchLocalMatchesAsync(
                                _searchService, query, fileLimit, appLimit, contextDirectory, localMatches, token, bypassExclusions: true);
                        }
                        await PerformStreamingSearchAsync(query, null, contextDirectory, isInlineSearchContext, fileLimit, appLimit, resultMapper, searchVersion, onResultsUpdated, token, localMatches, localSearchTask, onLocalServiceUnavailable, bypassExclusions);
                        return;
                    }
                }
                var streamingScope = tracker.IsActiveWindowExplorer ? searchScope : null;
                var streamingContextDirectory = isInlineSearchContext ? (!string.IsNullOrWhiteSpace(searchScope) ? searchScope : tracker.ActivePath ?? tracker.LastActiveExplorerPath) : tracker.LastActiveExplorerPath;
                await PerformStreamingSearchAsync(query, streamingScope, streamingContextDirectory, isInlineSearchContext, fileLimit, appLimit, resultMapper, searchVersion, onResultsUpdated, token, null, null, onLocalServiceUnavailable, bypassExclusions);
            }
            catch (OperationCanceledException) { }
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
                        {
                            onSearchStateChanged(false);
                        }
                    }
                }));
            }
        }, token);
    }

    private async Task PerformStreamingSearchAsync(
        string query,
        string? searchScope,
        string? contextDirectory,
        bool isInlineSearchContext,
        int fileLimit,
        int appLimit,
        Func<List<SearchResult>?, string?, List<AppSearchResult>> resultMapper,
        int searchVersion,
        Action<List<AppSearchResult>, string, bool> onResultsUpdated,
        CancellationToken token,
        List<AppSearchResult>? localMatches = null,
        Task? localSearchTask = null,
        Action? onLocalServiceUnavailable = null,
        bool bypassExclusions = false)
    {
        var streamedResponse = new List<SearchResult>();
        object responseLock = new();
        var streamedCount = 0;
        var streamDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // Both RenderSnapshot callers (the streaming result callback below, and the final call after
        // localSearchTask) already run on a background thread -- PerformStreamingSearchAsync executes
        // inside PerformSearch's Task.Run, which doesn't flow a SynchronizationContext, so every await
        // in this method resumes on a ThreadPool thread throughout. resultMapper (rank-sorts up to
        // every streamed result via SearchResultRankComparer, then builds one AppSearchResult
        // per row) and the rest of the snapshot-building logic below have no WPF/UI-thread dependency at
        // all -- they only read the locked snapshot and build plain lists. This used to run wrapped
        // inside the Dispatcher.BeginInvoke below regardless, meaning that CPU cost blocked the UI thread
        // on every streaming render for the full window's own broad queries. Now only the actual
        // UI-touching step (onResultsUpdated, which assigns FilteredResults and triggers sort/filter/
        // render) is marshaled, after re-checking staleness once more in case the search was superseded
        // while this thread was busy computing.
        // take: how many of the results received so far to paint. int.MaxValue for the final render,
        // ProgressiveRenderPlan's current cap for an intermediate one -- see that class for why an
        // intermediate render must not touch the whole snapshot.
        // Awaits the UI thread rather than posting and moving on. Fire-and-forget let this method run
        // far ahead of the thread that actually applies its output: the pump produced a paint every
        // 150ms while each one took the UI a second to apply, so the dispatcher queue grew without
        // bound, and since every queued callback holds its own full-size row list, hundreds of them
        // pinned gigabytes. A real run ended up 54 seconds and ~150 stale paints behind its own search,
        // still grinding through them long after the results were complete. Awaiting means at most one
        // paint is ever in flight, so the queue cannot build and neither can the memory behind it.
        async Task RenderSnapshotAsync(bool final, int take)
        {
            if (searchVersion != Volatile.Read(ref _searchVersion) || token.IsCancellationRequested)
                return;
            List<SearchResult> snapshot;
            var received = 0;
            lock (responseLock)
            {
                received = streamedResponse.Count;
                snapshot = take >= received
                    ? new List<SearchResult>(streamedResponse)
                    : streamedResponse.GetRange(0, take);
            }

            var uiResults = resultMapper(snapshot, contextDirectory);

            List<AppSearchResult>? localMatchesCopy = null;
            if (localMatches != null)
            {
                lock (localMatches)
                {
                    if (localMatches.Count > 0)
                    {
                        localMatchesCopy = new List<AppSearchResult>(localMatches);
                    }
                }
            }

            if (localMatchesCopy != null)
            {
                uiResults = InlineListSearchHelper.MergeLocalMatches(uiResults, localMatchesCopy, query);
            }

            if (final && uiResults.Count == 0)
                uiResults.Add(SearchResultMapper.CreateNoResultsResult(query));
            var statusText = "";
            if (uiResults.Count > 0)
                // The count RECEIVED, not the capped count rendered -- an intermediate render paints a
                // prefix, and reporting the prefix's length would make the status bar understate what
                // the search has actually found so far and then jump at the end.
                statusText = SearchResultMapper.FormatSearchStatus(0, received);
            else if (final)
                statusText = "No matching results";

            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (searchVersion != Volatile.Read(ref _searchVersion) || token.IsCancellationRequested)
                    return;
                onResultsUpdated(uiResults, statusText, final);
            }).Task.ConfigureAwait(false);
        }

        // Repaints on a fixed cadence for as long as results keep arriving, where this used to schedule
        // exactly one intermediate paint (nine results in, at the 40ms mark) and then show nothing more
        // until the entire search had finished. That was fine while the full window asked for a thousand
        // results; unbounded, a broad query spends seconds in the stream and the window sat on those
        // first nine rows for all of it, looking like the search had stalled rather than like it was
        // working. The cadence is deliberately slower than the first paint: 40ms to get something on
        // screen, then every 150ms, which is frequent enough to read as continuous growth and rare
        // enough that the render cost stays noise next to the stream itself.
        using var pumpCts = CancellationTokenSource.CreateLinkedTokenSource(token);
        var pumpToken = pumpCts.Token;
        var pump = Task.Run(async () =>
        {
            var plan = new ProgressiveRenderPlan();
            var interval = FirstRenderDelayMs;
            var sinceLastPaint = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                while (true)
                {
                    if (streamDone.Task.IsCompleted)
                        await Task.Delay(interval, pumpToken).ConfigureAwait(false);
                    else
                        // Woken early when the stream ends, so a search that resolves in a few
                        // milliseconds isn't held behind a cadence meant for one that takes seconds.
                        await Task.WhenAny(Task.Delay(interval, pumpToken), streamDone.Task).ConfigureAwait(false);
                    pumpToken.ThrowIfCancellationRequested();

                    var finished = streamDone.Task.IsCompleted;
                    var received = Volatile.Read(ref streamedCount);

                    var take = plan.NextRenderSize(received, sinceLastPaint.ElapsedMilliseconds);
                    if (take == 0)
                    {
                        // Nothing new to show. Once the stream has ended that is also true of every
                        // later tick -- no more results can arrive -- so hand over to the final render
                        // instead of idling on the cadence forever.
                        if (finished)
                            return;
                        interval = ProgressiveRenderIntervalMs;
                        continue;
                    }

                    // While draining, the cadence only exists to leave the UI thread room between
                    // paints: the backlog is already in memory and every remaining tick has real work,
                    // so holding the full 150ms between them just stretches the tail for no benefit.
                    interval = finished ? DrainRenderIntervalMs : ProgressiveRenderIntervalMs;

                    // Awaited inside the loop, so the interval above is time the UI gets ON TOP of
                    // however long the paint itself took, not time that overlaps it. That is what keeps
                    // the window able to process input between paints instead of being pinned by a
                    // queue of them. Awaiting the pump below then also guarantees no intermediate paint
                    // is still in flight once the final one starts, so the two can't reach the
                    // Dispatcher out of order and leave a truncated prefix on screen.
                    var paintClock = System.Diagnostics.Stopwatch.StartNew();
                    await RenderSnapshotAsync(final: false, take).ConfigureAwait(false);
                    plan.PaintCompleted(paintClock.ElapsedMilliseconds);
                    sinceLastPaint.Restart();
                }
            }
            catch (OperationCanceledException) { }
        }, pumpToken);

        try
        {
            await _searchService.SearchStreamingAsync(query, fileLimit, appLimit, searchScope, result =>
            {
                token.ThrowIfCancellationRequested();

                // Excludes a result whose source (local drive, network drive, WSL, folder index) was found
                // unreachable by this session's own SearchReachabilityGate.BeginSession probe -- see its own
                // comment on why that's a better fit here than either source type's existing periodic signal.
                if (!SearchReachabilityGate.IsResultReachable(result))
                    return;

                lock (responseLock)
                {
                    streamedResponse.Add(result);
                    streamedCount++;
                }
            }, token, () => _ = System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!token.IsCancellationRequested && searchVersion == Volatile.Read(ref _searchVersion))
                {
                    onLocalServiceUnavailable?.Invoke();
                }
            })), bypassExclusions);

            token.ThrowIfCancellationRequested();
            if (localSearchTask != null)
            {
                try
                {
                    await localSearchTask;
                }
                catch { }
            }
        }
        finally
        {
            // Flagged first, then awaited, so the pump gets to DRAIN the backlog rather than being cut
            // off at whatever it had reached: a search whose results all arrive faster than they can be
            // mapped leaves most of the set unpainted at this point, and cancelling here would dump all
            // of it into one final render -- the multi-second freeze the whole progressive path exists
            // to break up. Awaiting also means no intermediate render is still computing once the final
            // one begins, so the two can't reach the Dispatcher out of order.
            streamDone.TrySetResult();
            try { await pump.ConfigureAwait(false); } catch (OperationCanceledException) { }
        }

        await RenderSnapshotAsync(final: true, int.MaxValue).ConfigureAwait(false);
    }

    public void CancelPendingSearch()
    {
        try { _debounceCts?.Cancel(); _debounceCts?.Dispose(); _debounceCts = null; } catch { }
        lock (_searchCtsLock)
        {
            _searchCts?.Cancel();
            _searchCts?.Dispose();
            _searchCts = null;
        }
    }

    public void Dispose() { CancelPendingSearch(); _debounceCts?.Dispose(); }
}
