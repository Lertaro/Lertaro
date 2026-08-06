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
        Task? localSearchTask = null,
        Action? onLocalServiceUnavailable = null,
        bool bypassExclusions = false)
    {
        var streamedResponse = new List<SearchResult>();
        object responseLock = new();
        var streamedCount = 0;
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
                snapshot = take >= received
                    ? new List<SearchResult>(streamedResponse)
                    : streamedResponse.GetRange(0, take);
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
                    var take = plan.NextRenderSize(received, sinceLastPaint.ElapsedMilliseconds);
                    if (take == 0 && !localChanged)
                    {
                        if (finished)
                            return;
                        interval = ProgressiveRenderIntervalMs;
                        continue;
                    }

                    interval = finished ? DrainRenderIntervalMs : ProgressiveRenderIntervalMs;
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
}
