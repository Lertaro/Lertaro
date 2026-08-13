using Lertaro.App.ViewModels.Search.Mapping;
using Lertaro.Core;
using Lertaro.Core.Services.Search;

namespace Lertaro.App.ViewModels.Search;

// Split from SearchExecutionEngine solely to keep that orchestration class under the repository's per-file limit.
internal sealed class SearchStreamRenderer
{
    private const int FirstRenderDelayMs = 40;
    private const int ProgressiveRenderIntervalMs = 150;
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
        Func<int>? getLocalMatchCount = null,
        Task? localSearchTask = null,
        Task? globalSearchStartGate = null,
        Action? onLocalServiceUnavailable = null,
        bool bypassExclusions = false,
        bool resultMapperConsumesBatches = false,
        Action<int>? onReceivedCountUpdated = null)
    {
        var streamedResponse = new List<SearchResult>();
        object responseLock = new();
        var streamedCount = 0;
        // Full-window ranking accepts independent batches, so it never needs the already-copied prefix.
        // Quick and inline mappers still receive their complete snapshot on every render.
        var copiedCount = 0;
        var streamDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

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
            if (localMatches is { Count: > 0 })
                uiResults = InlineListSearchHelper.MergeLocalMatches(uiResults, localMatches, query);

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
            var interval = FirstRenderDelayMs;
            var sinceLastPaint = System.Diagnostics.Stopwatch.StartNew();
            // Local enumeration can produce its first matches before this pump starts. Begin at zero so
            // that already-arrived matches still trigger the first merged render, including its header.
            var renderedLocalVersion = 0;
            long firstSmallLocalUpdateMs = -1;
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

                    // A tiny Current Folder section followed immediately by its Global Search section makes
                    // the inline card resize twice. Give the global phase a short chance to finish so the
                    // common small-result case paints once; a slow global search still shows local matches
                    // promptly after the bounded delay.
                    if (take == 0 && localChanged && !finished && getLocalMatchCount != null)
                    {
                        if (firstSmallLocalUpdateMs < 0)
                            firstSmallLocalUpdateMs = sinceLastPaint.ElapsedMilliseconds;

                        var elapsed = sinceLastPaint.ElapsedMilliseconds - firstSmallLocalUpdateMs;
                        if (InlineSmallResultRenderDelay.ShouldDelay(getLocalMatchCount(), elapsed))
                        {
                            interval = Math.Max(1, InlineSmallResultRenderDelay.SettleDelayMs - (int)elapsed);
                            continue;
                        }
                    }

                    // The final render below contains the latest local snapshot, so an under-threshold
                    // completed stream never needs a visibly transient local-only paint first.
                    if (finished && take == 0 && getLocalMatchCount != null && (localSearchTask?.IsCompleted ?? true))
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
                        interval = ProgressiveRenderIntervalMs;
                        continue;
                    }

                    interval = finished ? DrainRenderIntervalMs : ProgressiveRenderIntervalMs;
                    var paintClock = System.Diagnostics.Stopwatch.StartNew();
                    await RenderSnapshotAsync(final: false, take).ConfigureAwait(false);
                    renderedLocalVersion = getLocalUpdateVersion?.Invoke() ?? renderedLocalVersion;
                    firstSmallLocalUpdateMs = -1;
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
            if (globalSearchStartGate != null)
                await globalSearchStartGate.ConfigureAwait(false);

            await _searchService.SearchStreamingAsync(query, fileLimit, appLimit, searchScope, result =>
            {
                token.ThrowIfCancellationRequested();
                if (!SearchReachabilityGate.IsResultReachable(result))
                    return;

                lock (responseLock)
                {
                    streamedResponse.Add(result);
                    streamedCount++;
                }
            }, token, () => _ = System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!token.IsCancellationRequested && searchVersion == _getSearchVersion())
                    onLocalServiceUnavailable?.Invoke();
            })), bypassExclusions).ConfigureAwait(false);

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
