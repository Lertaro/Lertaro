using Lertaro.Core.SearchIndex;

using Lertaro.Core.Services.Search;
namespace Lertaro.Core.Services.Plugin.DirectoryIndex;

/// <summary>
/// Answers a plugin's directory search: lists each registered directory out of the index (see
/// <see cref="IndexedDirectoryEnumerator"/>, which also decides when a real filesystem walk is needed)
/// and keeps the entries whose name matches the query. Kept separate from
/// <see cref="PluginDirectoryWatchRegistry"/>, which owns registration and FileSystemWatcher lifecycle
/// -- answering "what matches this query" is a different concern from "watch for changes."
/// <para>
/// There is deliberately no local-versus-network split here any more. There used to be one, and the two
/// halves quietly disagreed on everything that mattered: the local half ignored both the
/// <c>FilterPattern</c> and the <c>Recursive</c> flag the plugin registered with, capped itself at 200
/// results and applied the user's exclusion settings, while the network half honoured the pattern and
/// the flag, had no cap, and applied no exclusions. Same registration, different answers depending on
/// which drive the directory happened to live on.
/// </para>
/// </summary>
internal sealed class PluginDirectorySearcher
{
    public async Task<List<SearchResult>> SearchAsync(IReadOnlyList<MonitoredDir> dirs, string query, CancellationToken token)
    {
        var taskResults = await Task.WhenAll(dirs.Select(dir => SearchDirectoryAsync(dir, query, token))).ConfigureAwait(false);

        // Deduped across directories: a plugin is free to register a directory and something inside it,
        // and one file is one result either way.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var results = new List<SearchResult>();
        foreach (var result in taskResults.SelectMany(list => list))
        {
            if (seen.Add(result.Path))
                results.Add(result);
        }
        return results;
    }

    private static async Task<List<SearchResult>> SearchDirectoryAsync(MonitoredDir dir, string query, CancellationToken token)
    {
        var list = new List<SearchResult>();
        try
        {
            await IndexedDirectoryEnumerator.EnumerateAsync(dir.Path, dir.Recursive, dir.FilterPattern, result =>
            {
                if (MatchesQuery(result.Name, query))
                    list.Add(result);
            }, limit: 0, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.Log($"[IndexManager] Directory query failed for '{dir.Path}': {ex.Message}", LogLevel.Warn);
        }
        return list;
    }

    /// <summary>
    /// The host's own matching, so a plugin searching its directories finds what the same text would
    /// find anywhere else in the app -- fuzzy and alias-aware (pinyin included), not a substring test.
    /// An empty query keeps everything: the caller asked for its directories, and nothing to match on
    /// means nothing to exclude.
    /// </summary>
    internal static bool MatchesQuery(string name, string query)
        => string.IsNullOrWhiteSpace(query) || FuzzyMatcher.IsMatch(query, name);
}
