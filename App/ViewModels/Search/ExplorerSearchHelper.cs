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
        Action? onMatchesChanged = null,
        bool bypassExclusions = false) => Task.Run(async () =>
    {
        Logger.Log($"[ExplorerSearchHelper] Starting descendant search for query: '{query}' in scope: '{contextDirectory}'", LogLevel.Debug);
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
                onMatchesChanged?.Invoke();
            }, token, bypassExclusions: bypassExclusions);
            Logger.Log($"[ExplorerSearchHelper] Descendant search completed. Matches count: {matchCount}", LogLevel.Debug);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Logger.Log($"[ExplorerSearchHelper] Descendant search failed: {ex.Message}", LogLevel.Error);
        }
    }, token);

    internal static List<AppSearchResult> CreatePrioritizedSnapshot(
        IEnumerable<AppSearchResult> matches,
        string query,
        string contextDirectory)
    {
        var normalizedDirectory = NormalizeDirectory(contextDirectory);
        var allMatches = matches
            .Where(match => !string.Equals(NormalizeDirectory(match.FullPath), normalizedDirectory, StringComparison.OrdinalIgnoreCase));

        var direct = RankMatches(allMatches.Where(match => IsDirectChild(match.FullPath, normalizedDirectory)), query);
        var descendants = RankMatches(allMatches.Where(match => !IsDirectChild(match.FullPath, normalizedDirectory)), query);
        var snapshot = OrderByDirectoryTier(direct.Concat(descendants), normalizedDirectory);
        return snapshot;
    }

    internal static List<AppSearchResult> OrderByDirectoryTier(IEnumerable<AppSearchResult> rankedMatches, string contextDirectory)
    {
        var normalizedDirectory = NormalizeDirectory(contextDirectory);
        var snapshot = rankedMatches.OrderBy(match => IsDirectChild(match.FullPath, normalizedDirectory) ? 0 : 1).Take(50).ToList();
        for (var index = 0; index < snapshot.Count; index++)
            snapshot[index].Index = index;
        return snapshot;
    }

    private static List<AppSearchResult> RankMatches(IEnumerable<AppSearchResult> matches, string query)
    {
        var historySnapshot = SearchHistoryStore.Snapshot();
        var favoritePaths = new HashSet<string>(
            UserSettings.Load().Favorites.Select(f => SearchResultHelper.NormalizePath(
                f.Path.Length > 3 && f.Path[^1] == '\\' ? f.Path.TrimEnd('\\') : f.Path)),
            StringComparer.OrdinalIgnoreCase);
        var candidates = new List<SearchResultMapper.RankedCandidate>();

        foreach (var match in matches)
        {
            var lookupPath = match.FullPath.Length > 3 && match.FullPath[^1] == '\\' ? match.FullPath.TrimEnd('\\') : match.FullPath;
            var normalizedPath = SearchResultHelper.NormalizePath(match.FullPath);
            var hasHistory = historySnapshot.TryGetValue(lookupPath, out var priority);
            candidates.Add(new SearchResultMapper.RankedCandidate(
                match,
                IsCurated: hasHistory || favoritePaths.Contains(normalizedPath),
                hasHistory ? priority : int.MaxValue,
                TypeRank: int.MaxValue,
                FuzzyMatcher.ComputeMatchWeight(match.Name, query),
                normalizedPath));
        }

        return SearchResultMapper.RankAndDedupe(candidates);
    }

    private static bool IsDirectChild(string path, string normalizedDirectory) => string.Equals(
        NormalizeDirectory(Path.GetDirectoryName(NormalizeDirectory(path)) ?? string.Empty),
        normalizedDirectory,
        StringComparison.OrdinalIgnoreCase);

    private static string NormalizeDirectory(string path) => path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
