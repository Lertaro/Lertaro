using System.IO;
using Lertaro.Core;
using Lertaro.Core.Services.Search;
using Lertaro.Core.Wire;

namespace Lertaro.App.Services.Pipe;

// Split from AppSearchPipeService solely to keep it within the repository's per-file line limit.
internal static class AppSearchPipeSpaceEntries
{
    public static async Task WriteAsync(SearchService searchService, string? directory, Stream pipe)
    {
        try
        {
            var entries = await searchService.GetSpaceEntriesAsync(directory).ConfigureAwait(false);
            await PipeResponseBinarySerializer.WriteSpaceEntriesAsync(pipe, entries).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.Log($"[AppSearchPipeService] Failed to get space entries: {ex.Message}", LogLevel.Debug);
            await PipeResponseBinarySerializer.WriteErrorAsync(pipe, "Could not read indexed space entries.").ConfigureAwait(false);
        }
    }
}
