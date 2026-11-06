using Lertaro.PluginSdk.Helpers;
using Lertaro.App.ViewModels.Search.Dispatch;
using Lertaro.App.ViewModels.Search.Mapping;
using Lertaro.Core;
using Lertaro.Core.Services.Plugin.DirectoryIndex;
using Lertaro.Core.Services.Search;
using System.Windows.Threading;

namespace Lertaro.App.ViewModels.Search;

// Split from SearchExecutionEngine solely to keep that orchestration class under the repository's per-file limit.
internal sealed class SearchStreamRenderer
{
    // The first paint is held back only long enough for a few rows to exist -- see
    // ProgressiveRenderPlan.MinimumFirstRender, which is what actually decides whether a paint happens.
    //
    // Raised from 16 to 30 ms. The tick that fires at 16 ms usually finds the stream still empty (the first
    // row takes noticeably longer than one frame), so it painted nothing and the next real paint was the
    // FINISHING frame anyway; when it did catch a row or two it painted a partial list that the finishing
    // frame then replaced wholesale, because a different set of rows ranks differently. That second full pass
    // measured out as the single largest cost after the search itself -- most of what "typing feels laggy"
    // turned out to be. Waiting one more frame means the common query paints exactly once, with its real
    // result. A search slow enough to still be streaming at 30 ms keeps the old behaviour and paints
    // progressively.
    private const int FirstRenderDelayMs = 30;

    // Tick period while a snapshot-style search (quick, inline) is still streaming. The duty-cycle rule in
    // ProgressiveRenderPlan is what protects the UI thread from expensive paints, so this does not have to
    // be conservative -- and at 150ms it was a hard floor between updates: any query whose results arrived
    // over more than a moment showed them in 150ms steps.
    private const int SnapshotProgressiveRenderIntervalMs = 50;

    // The full window renders whole batches and can be showing hundreds of thousands of rows, so its own
    // paints are expensive by nature and are deliberately spaced out. Kept separate from the snapshot
    // interval above rather than lowered for everyone.
    private const int BatchProgressiveRenderIntervalMs = 150;

    private const int DrainRenderIntervalMs = 25;

    // Per-directory row budget for the high-priority re-searches. Large enough that a promoted folder's
    // best matches are never lost, small enough that the extra index work stays invisible while typing.
    private const int PriorityRootResultLimit = 50;
    private const int ShortcutSupplementLimit = 50;

    private readonly SearchService _searchService;
    private readonly Func<int> _getSearchVersion;

    public SearchStreamRenderer(SearchService searchService, Func<int> getSearchVersion)
    {
        _searchService = searchService;
        _getSearchVersion = getSearchVersion;
    }

    public async Task RenderAsync(
        string query,
        string? searchScope,
        string? contextDirectory,
        int fileLimit,
        int appLimit,
        Func<List<SearchResult>?, string?, List<AppSearchResult>> resultMapper,
        int searchVersion,
        Action<List<AppSearchResult>, string, bool> onResultsUpdated,
        CancellationToken token,
        Func<List<AppSearchResult>>? getLocalSnapshot = null,
        Func<int>? getLocalUpdateVersion = null,
        Task? localSearchTask = null,
        Action? onLocalServiceUnavailable = null,
        bool bypassExclusions = false,
        bool resultMapperConsumesBatches = false,
        Action<int>? onReceivedCountUpdated = null,
        FileFilterScopeDirective? scopeDirective = null,
        bool foldersOnly = false,
        IReadOnlyList<string>? priorityRoots = null,
        SearchCategoryFilter category = SearchCategoryFilter.All)
    {
        var streamedResponse = new List<SearchResult>();
        object responseLock = new();
        var streamedCount = 0;
        var perfStart = SearchPerf.Start();
        var lastArrivalMs = 0L;
        // Full-window ranking accepts independent batches, so it never needs the already-copied prefix.
        // Quick and inline mappers still receive their complete snapshot on every render.
        var copiedCount = 0;
        var streamDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var runShortcutSupplement = ScopedSearchRunner.ShouldRunShortcutSupplement(
            fileLimit, foldersOnly, searchScope, scopeDirective);
        // Set by Accumulate the moment a row the supplement would have contributed arrives; read once, after
        // the main query has streamed. Volatile because the engine hands rows in from its own threads.
        var shortcutSurfaced = 0;

        // Scoped searches answer from N per-folder engine queries, so the same file can arrive from two
        // overlapping configured folders -- dedupe by path at accumulation. High-priority and shortcut
        // supplement passes add additive sources alongside the main query, so they use the same path guard.
        var dedupePaths = scopeDirective is { Folders.Count: > 0 }
            || priorityRoots is { Count: > 0 }
            || runShortcutSupplement
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : null;
        var scopePatterns = scopeDirective is { Folders.Count: > 0 }
            ? FilterPatternHelper.SplitOrNullIfMatchAll(scopeDirective.FilterPattern)
            : null;

        var consumeTicks = 0L;

        void Accumulate(SearchResult result)
        {
            var consumeStart = SearchPerf.Start();
            try
            {
                token.ThrowIfCancellationRequested();
                if (!SearchReachabilityGate.IsResultReachable(result))
                    return;
                // The card over a dialog that takes only a folder offers folders, so everything else is dropped
                // before it can take a row. Filtered here rather than after ranking so the row budget, the
                // progressive paint sizes and the "N results" count all describe the rows the window may
                // actually list.
                if (foldersOnly && !result.IsDir)
                    return;

                // The filter pattern constrains FILE names; a folder always passes.
                if (scopePatterns != null && !result.IsDir && !FilterPatternHelper.Matches(result.Name, scopePatterns))
                    return;

                lock (responseLock)
                {
                    if (dedupePaths != null && !dedupePaths.Add(result.Path))
                        return;
                    if (runShortcutSupplement && ScopedSearchRunner.IsSupplementShortcutPath(result.Path))
                        Volatile.Write(ref shortcutSurfaced, 1);
                    streamedResponse.Add(result);
                    streamedCount++;
                    Volatile.Write(ref lastArrivalMs, (long)SearchPerf.Ms(perfStart));
                }
            }
            finally
            {
                Interlocked.Add(ref consumeTicks, SearchPerf.ElapsedTicks(consumeStart));
            }
        }

        // Frames are mapped from the surviving rows of the stream. When two consecutive frames carry the very
        // same row objects the mapper's whole input is identical, so its output is too -- see the reuse in
        // RenderSnapshotAsync. A late first frame is the case that matters: by the time it paints the search
        // has already finished, so the final frame would otherwise redo the entire ranking pass for an
        // unchanged list.
        List<SearchResult>? mappedSource = null;
        List<AppSearchResult>? mappedRows = null;

        static bool SameRows(List<SearchResult> left, List<SearchResult> right)
        {
            if (left.Count != right.Count)
                return false;
            for (var i = 0; i < left.Count; i++)
                if (!ReferenceEquals(left[i], right[i]))
                    return false;
            return true;
        }

        async Task RenderSnapshotAsync(bool final, int take)
        {
            if (searchVersion != _getSearchVersion() || token.IsCancellationRequested)
                return;

            List<SearchResult> snapshot;
            var received = 0;
            lock (responseLock)
            {
                received = streamedResponse.Count;
                snapshot = CopySnapshot(streamedResponse, take, resultMapperConsumesBatches, ref copiedCount);
            }

            // Skip the whole mapping pass when this frame carries exactly the rows the previous one did.
            // The copy is shallow on purpose: the list is mutated below (a no-results row on an empty final
            // frame) and must not be the cached instance.
            List<AppSearchResult> uiResults;
            if (mappedSource != null && SameRows(mappedSource, snapshot))
            {
                uiResults = new List<AppSearchResult>(mappedRows!);
            }
            else
            {
                uiResults = resultMapper(snapshot, contextDirectory);
                mappedSource = snapshot;
                mappedRows = new List<AppSearchResult>(uiResults);
            }
            var localMatches = getLocalSnapshot?.Invoke();
            // Not just when the folder contributed rows: the global results still have to be re-ordered so
            // the folder's subfolders come before the rest of the drive. getLocalSnapshot is non-null
            // exactly for the inline window, which is the only one that groups by proximity.
            if (localMatches != null)
                uiResults = InlineListSearchHelper.MergeLocalMatches(uiResults, localMatches, query, contextDirectory);

            if (final && uiResults.Count == 0)
                uiResults.Add(SearchResultMapper.CreateNoResultsResult(query));

            var statusText = uiResults.Count > 0
                ? SearchResultMapper.FormatSearchStatus(0, received)
                : string.Empty;

            // Normal, not Background. Background does let pending input invalidate this query first, but it
            // also queues behind every layout and render pass the previous frame caused -- measured, the rows
            // could sit in that queue well after the data was in hand, which reads as "it hangs". Input still
            // wins: it arrives at Input priority, above Normal. The version token below is what actually
            // discards a stale frame, so arriving sooner cannot show a superseded result.
            var dispatchStart = SearchPerf.Start();
            var enteredAt = 0L;
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                enteredAt = SearchPerf.Start();
                if (searchVersion != _getSearchVersion() || token.IsCancellationRequested)
                    return;
                onResultsUpdated(uiResults, statusText, final);
                if (!final)
                    onReceivedCountUpdated?.Invoke(received);
            }, DispatcherPriority.Normal, token).Task.ConfigureAwait(false);
            var finishedAt = SearchPerf.Start();
            SearchPerf.Log($"[render] dispatch final={final} queue={SearchPerf.MsBetween(dispatchStart, enteredAt):F0}ms run={SearchPerf.MsBetween(enteredAt, finishedAt):F0}ms");
        }

        using var pumpCts = CancellationTokenSource.CreateLinkedTokenSource(token);
        var pumpToken = pumpCts.Token;
        var pump = Task.Run(async () =>
        {
            var plan = new ProgressiveRenderPlan();
            var progressiveInterval = resultMapperConsumesBatches
                ? BatchProgressiveRenderIntervalMs
                : SnapshotProgressiveRenderIntervalMs;
            var interval = FirstRenderDelayMs;
            var sinceLastPaint = System.Diagnostics.Stopwatch.StartNew();
            // Local enumeration can produce its first matches before this pump starts. Begin at zero so
            // that already-arrived matches still trigger the first merged render, including its header.
            var renderedLocalVersion = 0;
            try
            {
                while (true)
                {
                    // Nothing to wait for once the stream has ended: the tick below would then find `finished`
                    // true and paint the final frame anyway, so sleeping first only delays that paint by the
                    // whole interval. Measured at 19-27 ms of dead time on every search whose results all
                    // arrived inside one interval -- which is most of them.
                    if (!streamDone.Task.IsCompleted)
                        await Task.WhenAny(Task.Delay(interval, pumpToken), streamDone.Task).ConfigureAwait(false);
                    pumpToken.ThrowIfCancellationRequested();

                    var finished = streamDone.Task.IsCompleted;
                    var received = Volatile.Read(ref streamedCount);
                    var localChanged = (getLocalUpdateVersion?.Invoke() ?? renderedLocalVersion) != renderedLocalVersion;

                    // The final render consumes every remaining batch and performs the full window's
                    // one exact rank sort. Draining the backlog through intermediate UI paints first
                    // only repeats work after the producer has already finished.
                    if (finished && resultMapperConsumesBatches)
                        return;

                    var take = plan.NextRenderSize(received, sinceLastPaint.ElapsedMilliseconds);

                    // The final render below contains the latest local snapshot, so an under-threshold
                    // completed stream never needs a visibly transient local-only paint first.
                    if (finished && take == 0 && localSearchTask?.IsCompleted == true)
                        return;

                    if (take == 0 && !localChanged)
                    {
                        if (!finished && onReceivedCountUpdated != null)
                        {
                            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                            {
                                if (searchVersion == _getSearchVersion() && !token.IsCancellationRequested)
                                    onReceivedCountUpdated?.Invoke(received);
                            }, DispatcherPriority.Background, pumpToken).Task.ConfigureAwait(false);
                        }
                        if (finished)
                            return;
                        interval = progressiveInterval;
                        continue;
                    }

                    interval = finished ? DrainRenderIntervalMs : progressiveInterval;
                    var paintClock = System.Diagnostics.Stopwatch.StartNew();
                    await RenderSnapshotAsync(final: false, take).ConfigureAwait(false);
                    renderedLocalVersion = getLocalUpdateVersion?.Invoke() ?? renderedLocalVersion;
                    plan.PaintCompleted(paintClock.ElapsedMilliseconds);
                    sinceLastPaint.Restart();
                }
            }
            catch (OperationCanceledException)
            {
            }
        }, pumpToken);

        // Started beside the main query rather than after it: each is scoped to one promoted folder, and
        // the client only needs the union before its final sort. Skipped when the caller narrowed the
        // search itself (path mode, explicit folder scope): those scopes are what the user asked for.
        // Owned outside the try so a failing main query still drains it instead of leaving the pipe open.
        var priorityTask = priorityRoots is { Count: > 0 } && !bypassExclusions && scopeDirective is not { Folders.Count: > 0 }
            ? ScopedSearchRunner.RunPriorityRootsAsync(
                _searchService, priorityRoots, query, PriorityRootResultLimit, appLimit, Accumulate, token)
            : null;
        Task? shortcutTask = null;

        try
        {
            if (scopeDirective is { Folders.Count: > 0 })
            {
                await ScopedSearchRunner.RunAsync(_searchService, scopeDirective, query, fileLimit, appLimit, Accumulate,
                    () => _ = System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (!token.IsCancellationRequested && searchVersion == _getSearchVersion())
                            onLocalServiceUnavailable?.Invoke();
                    })), bypassExclusions, token).ConfigureAwait(false);
            }
            else
            {
                await _searchService.SearchStreamingAsync(query, fileLimit, appLimit, searchScope, Accumulate, token, () => _ = System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (!token.IsCancellationRequested && searchVersion == _getSearchVersion())
                        onLocalServiceUnavailable?.Invoke();
                })), bypassExclusions, category: category).ConfigureAwait(false);
            }

            token.ThrowIfCancellationRequested();

            // Started after the main query, not beside it: the question this pass answers -- did a bounded
            // initials query crowd a shortcut out of the engine's cut -- only has an answer once that cut is
            // on the table. Running it unconditionally cost every keystroke a second settings load, a second
            // exclusion-rule build and a second scan of every drive and share.
            shortcutTask = runShortcutSupplement && Volatile.Read(ref shortcutSurfaced) == 0
                ? ScopedSearchRunner.RunShortcutSupplementAsync(
                    _searchService, query, ShortcutSupplementLimit, appLimit, Accumulate, token)
                : null;

            if (priorityTask != null)
                await priorityTask.ConfigureAwait(false);

            if (shortcutTask != null)
                await shortcutTask.ConfigureAwait(false);

            if (localSearchTask != null)
            {
                try
                {
                    await localSearchTask.ConfigureAwait(false);
                }
                catch
                {
                }
            }
        }
        finally
        {
            if (priorityTask != null)
            {
                try
                {
                    await priorityTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
            }

            if (shortcutTask != null)
            {
                try
                {
                    await shortcutTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
            }

            streamDone.TrySetResult();
            try
            {
                await pump.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        await RenderSnapshotAsync(final: true, int.MaxValue).ConfigureAwait(false);
        SearchPerf.Log($"[stream] results={Volatile.Read(ref streamedCount)} lastArrival={Volatile.Read(ref lastArrivalMs)}ms wall={SearchPerf.Ms(perfStart):F0}ms consume={SearchPerf.MsFromTicks(Volatile.Read(ref consumeTicks)):F0}ms");
    }

    internal static List<SearchResult> CopySnapshot(
        List<SearchResult> source,
        int take,
        bool onlyNew,
        ref int copiedCount)
    {
        var end = Math.Min(take, source.Count);
        if (!onlyNew)
            return end >= source.Count ? new List<SearchResult>(source) : source.GetRange(0, end);

        if (end <= copiedCount)
            return [];

        var start = copiedCount;
        copiedCount = end;
        return source.GetRange(start, end - start);
    }

}
