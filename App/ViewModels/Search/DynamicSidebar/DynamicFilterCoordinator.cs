using Lertaro.Core;
using Lertaro.PluginSdk.Abstractions;

namespace Lertaro.App.ViewModels.Search.DynamicSidebar;

// One projection owns publication. A new query, filter or sort cancels the previous computation.
internal sealed class DynamicFilterCoordinator
{
    private CancellationTokenSource? _pending;

    public void Cancel()
    {
        _pending?.Cancel();
        _pending = null;
    }

    public Task Apply(
        List<AppSearchResult> allResults,
        List<Func<ISearchResult, bool>> activeFilters,
        Func<IEnumerable<AppSearchResult>, CancellationToken, Task<List<AppSearchResult>>> sort,
        Func<List<AppSearchResult>> currentResults,
        Action<List<AppSearchResult>> render,
        Action<bool> setBusy,
        bool runInBackground = true)
    {
        Cancel();
        var request = new CancellationTokenSource();
        _pending = request;
        setBusy(true);
        return ApplyAsync();

        async Task<List<AppSearchResult>> ComputeAsync()
        {
            IEnumerable<AppSearchResult> rows = allResults;
            if (activeFilters.Count > 0)
            {
                var kept = new List<AppSearchResult>();
                foreach (var row in allResults)
                {
                    request.Token.ThrowIfCancellationRequested();
                    var matches = true;
                    foreach (var filter in activeFilters)
                    {
                        if (filter(row)) continue;
                        matches = false;
                        break;
                    }
                    if (matches) kept.Add(row);
                }
                rows = kept;
            }
            request.Token.ThrowIfCancellationRequested();
            return await sort(rows, request.Token).ConfigureAwait(false);
        }

        async Task ApplyAsync()
        {
            try
            {
                var rows = runInBackground
                    ? await Task.Run(ComputeAsync, request.Token)
                    : await ComputeAsync();
                if (!request.IsCancellationRequested && ReferenceEquals(_pending, request)
                    && ReferenceEquals(currentResults(), allResults))
                    render(rows);
            }
            catch (Exception) when (request.IsCancellationRequested) { }
            catch (Exception ex) { Logger.Log($"[SearchFilters] Projection failed: {ex}", LogLevel.Error); }
            finally
            {
                if (ReferenceEquals(_pending, request))
                {
                    _pending = null;
                    setBusy(false);
                }
                request.Dispose();
            }
        }
    }
}
