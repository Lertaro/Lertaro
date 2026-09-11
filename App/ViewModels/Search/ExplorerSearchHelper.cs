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
        Logger.Log($"[ExplorerSearchHelper] Starting local search for query: '{query}' in scope: '{contextDirectory}'", LogLevel.Debug);
        var matchCount = 0;

        // The window's own folder first, from a direct one-level listing: this is the cheapest and
        // most reliable source for "files right here", and running it before the subtree search means
        // they appear immediately instead of waiting behind (and possibly losing the candidate cap to)
        // deeper results. Duplicates with the scoped search below collapse in RankAndDedupe.
        try
        {
            DirectChildrenLocator.MatchInto(contextDirectory, query, fileLimit, result =>
            {
                lock (localMatches)
                {
                    localMatches.Add(SearchResultMapper.CreateUiResult(result, query, localMatches.Count, isApplication: false, contextDirectory));
                    matchCount++;
                }
                onMatchesChanged?.Invoke();
            }, token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Logger.Log($"[ExplorerSearchHelper] Direct-child locate failed: {ex.Message}", LogLevel.Error);
        }

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

    // The inline window's Current Folder section. Directory proximity is the PRIMARY key -- files
    // directly in the window's own folder, then files in subfolders by depth (nearest first), then
    // anything outside it (DirectoryProximity.Tier). Learned history rows lead their own level: MergeRows
    // offers them first and the sort is stable, so it reorders ACROSS levels without disturbing order
    // inside one. The 50-row cap is applied only after that sort, so a direct child can never be crowded
    // out by deeper history or deep matches.
    internal static List<AppSearchResult> CreateLocalSnapshot(
        IEnumerable<AppSearchResult> matches,
        IReadOnlyList<SearchResultMapper.RankedCandidate> learned,
        string query,
        string contextDirectory,
        int limit = 50)
    {
        var normalizedDirectory = DirectoryProximity.Normalize(contextDirectory);
        var ordinary = RankMatches(
            matches.Where(match => !string.Equals(DirectoryProximity.Normalize(match.FullPath), normalizedDirectory, StringComparison.OrdinalIgnoreCase)),
            FuzzyQuery.Parse(query));

        // Learned rows are those the history merge contributed rather than the ordinary set; tracked by
        // normalized path, which is also the merge's own identity for a row.
        var learnedPaths = new HashSet<string>(
            learned.Select(c => SearchResultHelper.NormalizePath(c.Result.FullPath)),
            StringComparer.OrdinalIgnoreCase);

        var rankByPath = new Dictionary<string, MatchRank>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in ordinary)
            rankByPath[candidate.NormalizedPath] = candidate.Match;
        foreach (var candidate in learned)
            rankByPath.TryAdd(candidate.NormalizedPath, candidate.Match);

        // int.MaxValue on purpose: the cap belongs after the proximity sort below, not before it.
        var merged = HistorySearchCandidateMapper.MergeRows(learned, ordinary.Select(c => c.Result).ToList(), int.MaxValue);
        var ranked = merged
            .Select(result =>
            {
                var path = SearchResultHelper.NormalizePath(result.FullPath);
                return new RankedRow(result, rankByPath.TryGetValue(path, out var rank) ? rank : TopTier);
            })
            .ToList();

        var snapshot = OrderByDirectoryTier(ranked, normalizedDirectory).Take(limit).ToList();
        for (var index = 0; index < snapshot.Count; index++)
            snapshot[index].Index = index;
        return snapshot;
    }

    // Rank given to a row no match produced (a curated history entry): top tier, so it sorts as the
    // "best match" its leading position already means.
    private static readonly MatchRank TopTier = new(MatchRank.TierName, 0, 1.0);

    // Rows ordered by proximity to the window's folder, then by the rule that suits their level:
    //
    //   * DIRECT children (tier 0) -- folders first, then files (Explorer's own convention, and the user
    //     asked for the two groups NOT to interleave), each group in match START, then match TIER, then
    //     Explorer's own file-name order.
    //
    //     The coverage WEIGHT is deliberately not used here, unlike the global ladder: its coverage term is
    //     matched-chars / name-length, which is bound to name length, so for the query "l" it ranked
    //     Lertaro (1/7), then lx-music (1/8), then LRC maker (1/9) -- "the shorter name first", the reported
    //     symptom. Explorer's own name order replaces it, which is also what the user is comparing against
    //     in the one view that shows a single real folder. Tier still sits last of the match keys, as it
    //     does everywhere else.
    //   * Descendants and anything outside the folder -- the MatchRank order RankAndDedupe already
    //     established (start, weight, tier), merely grouped by proximity depth. These are rows the user
    //     did not just see in the folder listing, so relevance is the useful order.
    internal static List<AppSearchResult> OrderByDirectoryTier(IEnumerable<RankedRow> rankedMatches, string contextDirectory)
    {
        var direct = new List<RankedRow>();
        var others = new List<RankedRow>();
        foreach (var row in rankedMatches)
        {
            if (DirectoryProximity.Tier(row.Result.FullPath, contextDirectory) == 0)
                direct.Add(row);
            else
                others.Add(row);
        }

        // OrderBy is stable, so equal proximity depths keep the MatchRank order they arrived in.
        var orderedOthers = others
            .OrderBy(row => DirectoryProximity.Tier(row.Result.FullPath, contextDirectory))
            .Select(row => row.Result);

        // Direct children first, folders before files, then start -> tier -> Explorer name order.
        var orderedDirect = direct
            .OrderBy(row => row.Result.IsDir ? 0 : 1)
            .ThenBy(row => row.Match.Start)
            .ThenBy(row => row.Match.Tier)
            .ThenBy(row => row.Result.Name, NaturalNameComparer.Instance)
            .Select(row => row.Result);

        return orderedDirect.Concat(orderedOthers).ToList();
    }

    // A result paired with the match rank that placed it, so the ordering above can consult the rank even
    // though RankAndDedupe has already consumed it into an ordered list.
    internal readonly record struct RankedRow(AppSearchResult Result, MatchRank Match);

    private static List<SearchResultMapper.RankedCandidate> RankMatches(IEnumerable<AppSearchResult> matches, FuzzyQuery fuzzy)
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
                fuzzy.Rank(match.Name),
                normalizedPath));
        }

        // Ordered by the same ladder the merged list uses, but returned as candidates so the caller still
        // has each row's MatchRank to order by afterwards.
        return SearchResultMapper.RankCandidates(candidates);
    }
}
