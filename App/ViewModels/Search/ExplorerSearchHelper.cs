using System.IO;
using Lertaro.Core;

using Lertaro.Core.Services.Search;

using Lertaro.Core.SearchIndex;
using Lertaro.App.ViewModels.Search.Mapping;
namespace Lertaro.App.ViewModels.Search;

public static class ExplorerSearchHelper
{
    public static Task SearchLocalMatchesAsync(
        SearchService searchService,
        string query,
        int fileLimit,
        int appLimit,
        string contextDirectory,
        List<AppSearchResult> localMatches,
        CancellationToken token,
        bool bypassExclusions = false) => Task.Run(async () =>
    {
        Logger.Log($"[ExplorerSearchHelper] Starting local search for query: '{query}' in scope: '{contextDirectory}'", LogLevel.Debug);
        var matchCount = 0;
        try
        {
            await searchService.SearchStreamingAsync(query, fileLimit, appLimit, contextDirectory, result =>
            {
                lock (localMatches)
                {
                    localMatches.Add(SearchResultMapper.CreateUiResult(result, query, localMatches.Count, isApplication: false, contextDirectory));
                    matchCount++;
                }
            }, token, bypassExclusions: bypassExclusions);
            Logger.Log($"[ExplorerSearchHelper] Local search completed. Matches count: {matchCount}", LogLevel.Debug);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Logger.Log($"[ExplorerSearchHelper] Local search failed: {ex.Message}", LogLevel.Error);
        }

        lock (localMatches)
        {
            var normalizedDir = contextDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            // Filter out the scope directory itself — the backend's StartsWith filter matches it,
            // but it should never appear inside the "Current Folder" results group.
            localMatches.RemoveAll(x =>
                string.Equals(
                    x.FullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    normalizedDir,
                    StringComparison.OrdinalIgnoreCase));

            // Same ranking SearchResultMapper.BuildQuickResults uses for its own "Global Search" tier
            // (favorites/history first, then match weight, then shorter path) -- a file scores the
            // same way regardless of which of the inline window's two sections it lands in, rather
            // than this "Current Folder" section using its own folder-depth-first rule.
            var historySnapshot = SearchHistoryStore.Snapshot();
            var favoritePaths = new HashSet<string>(
                UserSettings.Load().Favorites.Select(f => SearchResultHelper.NormalizePath(
                    f.Path.Length > 3 && f.Path[^1] == '\\' ? f.Path.TrimEnd('\\') : f.Path)),
                StringComparer.OrdinalIgnoreCase);

            var candidates = new List<SearchResultMapper.RankedCandidate>(localMatches.Count);
            foreach (var match in localMatches)
            {
                var lookupPath = match.FullPath.Length > 3 && match.FullPath[^1] == '\\' ? match.FullPath.TrimEnd('\\') : match.FullPath;
                var normalizedPath = SearchResultHelper.NormalizePath(match.FullPath);
                var hasHistory = historySnapshot.TryGetValue(lookupPath, out var priority);
                var isFavorite = favoritePaths.Contains(normalizedPath);
                candidates.Add(new SearchResultMapper.RankedCandidate(
                    match,
                    IsCurated: hasHistory || isFavorite,
                    hasHistory ? priority : int.MaxValue,
                    TypeRank: int.MaxValue, // the type-priority order is quick-window only; never consulted here
                    FuzzyMatcher.ComputeMatchWeight(match.Name, query),
                    normalizedPath));
            }

            var sorted = SearchResultMapper.RankAndDedupe(candidates);

            localMatches.Clear();
            localMatches.AddRange(sorted.Take(50));
            for (var idx = 0; idx < localMatches.Count; idx++)
            {
                localMatches[idx].Index = idx;
            }
        }
    }, token);
}
