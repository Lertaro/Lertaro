using Lertaro.Core.Indexer.Usn;

using Lertaro.Core.Services.Search;

namespace Lertaro.App.Services;

internal static class SearchIndexBootstrapHelper
{
    public static async Task<UsnIndexer.IndexerStatus> EnsureInitializedAsync(SearchService searchService, bool forceRebuild = false)
    {
        if (forceRebuild)
        {
            await searchService.InitializeOrLoadIndexAsync(true).ConfigureAwait(false);
            return new UsnIndexer.IndexerStatus { State = "force-rebuild" };
        }

        var status = await WaitForStatusAsync(searchService).ConfigureAwait(false);
        if (status.State != "ready")
        {
            await searchService.InitializeOrLoadIndexAsync(false).ConfigureAwait(false);
        }

        return await WaitForStatusAsync(searchService).ConfigureAwait(false);
    }

    private static async Task<UsnIndexer.IndexerStatus> WaitForStatusAsync(SearchService searchService)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var status = await searchService.GetStatusAsync().ConfigureAwait(false);
            if (status.State != "error")
                return status;

            await Task.Delay(200).ConfigureAwait(false);
        }

        return new UsnIndexer.IndexerStatus { State = "error" };
    }
}
