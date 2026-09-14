using System.Text;
using Lertaro.Core.Services.Installation;

namespace Lertaro.Cli.Search;

internal static class NonInteractiveSearchCommand
{
    public static async Task<int> RunAsync(NonInteractiveSearchOptions options)
    {
        var pipeName = AppSearchPipeClient.PipeNameFor(CurrentUserIdentity.SessionHash);
        try
        {
            await AppSearchPipeClient.ProbeAsync(pipeName);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[error] Could not reach the Lertaro App's search pipe within {AppSearchPipeClient.ConnectTimeoutMs}ms ({ex.GetType().Name}: {ex.Message}) -- is the Lertaro App running?");
            return 3;
        }

        try
        {
            var streamed = await AppSearchPipeClient.SearchAsync(
                pipeName,
                options.Query,
                static _ => { },
                CancellationToken.None,
                options.DirectoryFilter);
            var results = NonInteractiveSearchResults.Prepare(
                streamed.Select(item => item.Result), options.Query, options);

            Console.OutputEncoding = new UTF8Encoding(false);
            if (options.Json)
                Console.WriteLine(NonInteractiveSearchJson.Serialize(results));
            else
                foreach (var result in results)
                    Console.WriteLine(result.Path);

            return results.Count == 0 ? 1 : 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[error] Search failed ({ex.GetType().Name}: {ex.Message}).");
            return 1;
        }
    }
}
