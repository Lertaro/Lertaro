using Lertaro.App.ViewModels.Search.Dispatch;
using Lertaro.Core;
using Lertaro.Core.Services.Search;

namespace Lertaro.App.ViewModels.Search;

// Keeps the per-folder search fan-out out of SearchStreamRenderer so each file stays below the
// repository's line limit. The runner owns only orchestration; SearchService still owns all index work.
internal static class ScopedSearchRunner
{
    public static async Task RunAsync(
        SearchService searchService,
        FileFilterScopeDirective directive,
        string query,
        int fileLimit,
        int appLimit,
        Action<SearchResult> onResult,
        Action onLocalSearchFailed,
        bool bypassExclusions,
        CancellationToken token)
    {
        var failureReported = 0;
        void ReportLocalSearchFailedOnce()
        {
            if (Interlocked.Exchange(ref failureReported, 1) == 0)
                onLocalSearchFailed();
        }

        var tasks = directive.Folders.Select(folder => RunFolderAsync(
            searchService, folder, query, fileLimit, appLimit, onResult,
            ReportLocalSearchFailedOnce, bypassExclusions, directive.FilterPattern, token)).ToArray();

        try
        {
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Superseded or window-closed: scoped searches stop silently like unscoped searches.
        }
    }

    private static async Task RunFolderAsync(
        SearchService searchService,
        string folder,
        string query,
        int fileLimit,
        int appLimit,
        Action<SearchResult> onResult,
        Action onLocalSearchFailed,
        bool bypassExclusions,
        string fileNameFilter,
        CancellationToken token)
    {
        try
        {
            await searchService.SearchStreamingAsync(
                query, fileLimit, appLimit, folder, onResult, token,
                onLocalSearchFailed, bypassExclusions, fileNameFilter: fileNameFilter).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.Log($"[ScopedSearchRunner] Search of '{folder}' failed: {ex.Message}", LogLevel.Warn);
        }
    }
}
