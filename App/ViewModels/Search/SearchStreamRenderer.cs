using Lertaro.App.ViewModels.Search.Dispatch;
using Lertaro.App.ViewModels.Search.Mapping;
using Lertaro.Core;
using Lertaro.Core.Services.Plugin.DirectoryIndex;
using Lertaro.Core.Services.Search;

namespace Lertaro.App.ViewModels.Search;

// Split from SearchExecutionEngine solely to keep that orchestration class under the repository's per-file limit.
internal sealed class SearchStreamRenderer
{
    // The first paint is held back only long enough for a few rows to exist -- see
    // ProgressiveRenderPlan.MinimumFirstRender, which is what actually decides whether a paint happens.
    // This is a floor on top of that, so it is kept to about one frame at 60Hz: it used to be 40ms, which
    // was 24ms of pure waiting on every search's first visible row, and the thing the user feels most
    // directly while typing.
    private const int FirstRenderDelayMs = 16;

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
        FileFilterScopeDirective? scopeDirective = null)
    {
        var streamedResponse = new List<SearchResult>();
        object responseLock = new();
        var streamedCount = 0;
        // Full-window ranking accepts independent batches, so it never needs the already-copied prefix.
        // Quick and inline mappers still receive their complete snapshot on every render.
        var copiedCount = 0;
        var streamDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // Scoped searches answer from N per-folder engine queries, so the same file can arrive from two
        // overlapping configured folders -- dedupe by path at accumulation.
        var seenPaths = scopeDirective is { Folders.Count: > 0 } ? new HashSet<string>(StringComparer.OrdinalIgnoreCase) : null;
        var scopePatterns = scopeDirective is { Folders.Count: > 0 }
            ? FilterPatternHelper.SplitOrNullIfMatchAll(scopeDirective.FilterPattern)
            : null;

        void Accumulate(SearchResult result)
        {
            token.ThrowIfCancellationRequested();
            if (!SearchReachabilityGate.IsResultReachable(result))
                return;

            // The filter pattern constrains FILE names; a folder always passes (same semantics the
            // pattern had when the filter plugin enumerated directories itself).
            if (scopePatterns != null && !result.IsDir && !FilterPatternHelper.Matches(result.Name, scopePatterns))
                return;

            lock (responseLock)
            {
                if (seenPaths != null && !seenPaths.Add(result.Path))
                    return;
                streamedResponse.Add(result);
                streamedCount++;
            }
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

            var uiResults = resultMapper(snapshot, contextDirectory);
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
                : final ? "No matching results" : string.Empty;

            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (searchVersion != _getSearchVersion() || token.IsCancellationRequested)
                    return;
                onResultsUpdated(uiResults, statusText, final);
                if (!final)
                    onReceivedCountUpdated?.Invoke(received);
            }).Task.ConfigureAwait(false);
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
                    if (streamDone.Task.IsCompleted)
                        await Task.Delay(interval, pumpToken).ConfigureAwait(false);
                    else
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
                        if (!finished)
                        {
                            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                            {
                                if (searchVersion == _getSearchVersion() && !token.IsCancellationRequested)
                                    onReceivedCountUpdated?.Invoke(received);
                            }).Task.ConfigureAwait(false);
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
                })), bypassExclusions).ConfigureAwait(false);
            }

            token.ThrowIfCancellationRequested();
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
