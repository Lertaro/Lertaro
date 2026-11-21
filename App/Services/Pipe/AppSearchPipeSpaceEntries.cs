using System.IO;
using Lertaro.Core;
using Lertaro.Core.IndexV2.Space;
using Lertaro.Core.Services.Search;
using Lertaro.Core.Wire;

namespace Lertaro.App.Services.Pipe;

// Streams indexed space-analysis entries through the App search pipe.
internal static class AppSearchPipeSpaceEntries
{
    public static async Task WriteAsync(SearchService searchService, string? directory, Stream pipe,
        SpaceEntryFilters filters = SpaceEntryFilters.None)
    {
        try
        {
            var entries = await searchService.GetSpaceEntriesAsync(directory, default, filters).ConfigureAwait(false);
            await PipeResponseBinarySerializer.WriteSpaceEntriesAsync(pipe, entries).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.Log($"[AppSearchPipeService] Failed to get space entries: {ex.Message}", LogLevel.Debug);
            await PipeResponseBinarySerializer.WriteErrorAsync(pipe, "Could not read indexed space entries.").ConfigureAwait(false);
        }
    }
}
