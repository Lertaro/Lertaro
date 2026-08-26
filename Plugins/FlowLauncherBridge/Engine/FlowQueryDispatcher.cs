using Flow.Launcher.Plugin;

namespace Lertaro.Plugins.FlowLauncherBridge.Engine;

/// <summary>
/// Routes queries to appropriate Flow plugins based on action keywords or global wildcard registration.
/// </summary>
public class FlowQueryDispatcher
{
    private readonly FlowPluginHost _host;

    public FlowQueryDispatcher(FlowPluginHost host) => _host = host;

    public Query ParseQuery(string rawInput)
    {
        var trimmed = rawInput?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(trimmed))
        {
            return new Query
            {
                OriginalQuery = rawInput ?? string.Empty,
                TrimmedQuery = string.Empty,
                Search = string.Empty,
                SearchTerms = [],
                ActionKeyword = string.Empty
            };
        }

        var firstSpace = trimmed.IndexOf(' ');
        var firstWord = firstSpace > 0 ? trimmed[..firstSpace] : trimmed;

        if (_host.KeywordPlugins.TryGetValue(firstWord, out _))
        {
            var search = firstSpace > 0 ? trimmed[(firstSpace + 1)..].TrimStart() : string.Empty;
            return new Query
            {
                OriginalQuery = rawInput ?? string.Empty,
                TrimmedQuery = trimmed,
                ActionKeyword = firstWord,
                Search = search,
                SearchTerms = search.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            };
        }

        return new Query
        {
            OriginalQuery = rawInput ?? string.Empty,
            TrimmedQuery = trimmed,
            ActionKeyword = string.Empty,
            Search = trimmed,
            SearchTerms = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries)
        };
    }

    public async Task<List<Result>> DispatchQueryAsync(string rawInput, CancellationToken token = default)
    {
        var query = ParseQuery(rawInput);
        var targetPlugins = GetTargetPlugins(query.ActionKeyword);

        if (targetPlugins.Count == 0)
            return [];

        var tasks = targetPlugins.Select(async pair =>
        {
            try
            {
                var results = await pair.Plugin.QueryAsync(query, token);
                if (results != null)
                {
                    foreach (var res in results)
                    {
                        if (string.IsNullOrEmpty(res.PluginDirectory))
                            res.PluginDirectory = pair.Metadata.PluginDirectory;
                        res.OriginQuery = query;
                    }
                    return results;
                }
            }
            catch (OperationCanceledException)
            {
                // Normal query cancellation
            }
            catch
            {
                // Suppress plugin query exceptions to protect launcher stability
            }
            return (List<Result>)[];
        });

        var resultsArray = await Task.WhenAll(tasks);
        return resultsArray.SelectMany(r => r).OrderByDescending(r => r.Score).ToList();
    }

    private IReadOnlyList<PluginPair> GetTargetPlugins(string actionKeyword)
    {
        if (!string.IsNullOrEmpty(actionKeyword) && _host.KeywordPlugins.TryGetValue(actionKeyword, out var specificPlugins))
        {
            return specificPlugins.Where(pair => !pair.Metadata.Disabled).ToList();
        }

        return _host.GlobalPlugins.Where(pair => !pair.Metadata.Disabled).ToList();
    }
}
