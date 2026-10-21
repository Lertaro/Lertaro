using Lertaro.Core;
using Lertaro.Core.SearchIndex;
using PriorityVerdict = Lertaro.Core.ExclusionRuleSet.PriorityVerdict;

namespace Lertaro.App.ViewModels.Search.Mapping;

// What this window hands the ranking kernel and what it does with the answer. Every ranking rule -- the four
// sections, the score, the key order inside each section and the shared path tail -- lives in Core
// (RankPolicy, RankKeyComparer), so the quick window and the full window cannot disagree; see
// dev-docs/search-ranking-redesign.md 3.2-3.3 for the numbers and their reasons.
//
// Only two things are here: which row owns a path when several describe the same one, and where each row's
// signals come from. Merging happens before the history budget, so a file the index returned and the same
// file in the history share one row -- that is what lets an indexed row be lifted by its own history.
internal static class RankedCandidateOrdering
{
    private const string ApplicationKind = "Application";

    public static List<SearchResultMapper.RankedCandidate> Order(
        List<SearchResultMapper.RankedCandidate> candidates,
        RankContext? context = null,
        string? query = null)
    {
        if (candidates.Count == 0)
            return [];

        var rows = MergeByPath(candidates);
        var signals = new RankSignals[rows.Length];
        for (var i = 0; i < rows.Length; i++)
            signals[i] = ToSignals(rows[i], context);

        var budget = RankPolicy.BuildHistoryBudget(
            signals,
            context?.HistoryFiveMax ?? RankPolicy.HistoryFiveMax,
            context?.HistoryThreeMax ?? RankPolicy.HistoryThreeMax);
        var keys = new RankKey[rows.Length];
        for (var i = 0; i < rows.Length; i++)
        {
            var tier = budget.GetValueOrDefault(signals[i].NormalizedPath, HistoryTier.None);
            keys[i] = RankPolicy.BuildKey(signals[i], tier);
            // The "frequent" badge: only the two lifted history tiers are a habit. A history-tail row
            // (fewer than three effective opens) is merely known, so it gets no badge.
            rows[i].Result.IsFrequent = tier >= HistoryTier.H3;
        }

        Array.Sort(keys, rows, RankKeyComparer.Instance);
        RecordPage(query, rows, keys);
        return [.. rows];
    }

    // The calibration sample: the leading rows of the page the user is about to see, ranked. Nothing is
    // allocated for it while the log is off.
    private static void RecordPage(string? query, SearchResultMapper.RankedCandidate[] rows, RankKey[] keys)
    {
        if (query is null || !RankingEventLog.IsEnabled)
            return;

        var taken = Math.Min(rows.Length, RankingEventLog.DisplayRowLimit);
        var page = new RankingLogRow[taken];
        for (var i = 0; i < taken; i++)
            page[i] = new RankingLogRow(rows[i].NormalizedPath, i + 1, keys[i].Section, keys[i].Score);

        RankingEventLog.RecordQuery(query, page);
    }

    private static SearchResultMapper.RankedCandidate[] MergeByPath(
        List<SearchResultMapper.RankedCandidate> candidates)
    {
        var merged = new List<SearchResultMapper.RankedCandidate>(candidates.Count);
        var slotByPath = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates)
        {
            var key = PathKey(candidate);
            if (!slotByPath.TryGetValue(key, out var slot))
            {
                slotByPath[key] = merged.Count;
                merged.Add(candidate);
                continue;
            }

            merged[slot] = PreferRow(merged[slot], candidate);
        }

        return [.. merged];
    }

    // One path, several views: an indexed shortcut row ("File") and a provider's application row describe the
    // same on-disk .lnk, and a history row can name a file the index also returned. Only the application row
    // can launch the app, so it owns its path -- leaving the choice to the text keys used to hand the path to
    // the plain-file view, which came back named "....lnk" with a file icon.
    //
    // Favorite and history are facts to combine, not reasons to pick a row, so PreferRow never looks at
    // HistoryCount: the two views of one path share one score, and the merge below decides what it is.
    private static SearchResultMapper.RankedCandidate PreferRow(
        SearchResultMapper.RankedCandidate a,
        SearchResultMapper.RankedCandidate b)
    {
        var better = CompareRows(a, b);
        var winner = better <= 0 ? a : b;

        return winner with
        {
            IsFavorite = a.IsFavorite || b.IsFavorite,
            HistoryCount = Math.Max(a.HistoryCount, b.HistoryCount),
            TypeRank = Math.Min(a.TypeRank, b.TypeRank),
            // Only a path every view agrees is history-only stays that way: once the index returned the
            // file, the row is a real name match with history on top.
            IsHistoryOnly = a.IsHistoryOnly && b.IsHistoryOnly,
        };
    }

    private static int CompareRows(
        SearchResultMapper.RankedCandidate a,
        SearchResultMapper.RankedCandidate b)
    {
        var byKind = (IsApplication(a) ? 0 : 1).CompareTo(IsApplication(b) ? 0 : 1);
        if (byKind != 0)
            return byKind;

        var byFavorite = (a.IsFavorite ? 0 : 1).CompareTo(b.IsFavorite ? 0 : 1);
        if (byFavorite != 0)
            return byFavorite;

        var byHistory = (a.IsHistoryOnly ? 1 : 0).CompareTo(b.IsHistoryOnly ? 1 : 0);
        if (byHistory != 0)
            return byHistory;

        var byQuality = MatchRank.CompareQuality(a.Match, b.Match);
        if (byQuality != 0)
            return byQuality;

        var byType = a.TypeRank.CompareTo(b.TypeRank);
        if (byType != 0)
            return byType;

        // Fully tied rows keep the one that arrived first, so the merge never depends on enumeration order.
        return SearchNameOrderComparer.Instance.Compare(a.Result.Name, b.Result.Name);
    }

    private static bool IsApplication(SearchResultMapper.RankedCandidate candidate) =>
        string.Equals(candidate.Result.ResultKind, ApplicationKind, StringComparison.Ordinal);

    private static string PathKey(SearchResultMapper.RankedCandidate candidate) =>
        candidate.NormalizedPath.TrimEnd('\\');

    private static RankSignals ToSignals(SearchResultMapper.RankedCandidate c, RankContext? context)
    {
        var path = PathKey(c);
        var verdict = context?.PriorityResolver?.Invoke(path, c.Result.IsDir)
                      ?? new PriorityVerdict(IndexRuleEffect.Normal, false);

        // The snapshot is authoritative when it has the path, including a decayed-to-zero entry: falling
        // back to the raw open count there would resurrect a score the decay already retired. Only rows the
        // caller never scored (tests, Windows Recent) derive from HistoryCount.
        var historyScore = context is not null && context.HistoryScoreX100.TryGetValue(path, out var known)
            ? known
            : Math.Min(10, Math.Max(0, c.HistoryCount)) * 100;

        return new RankSignals(
            Match: c.IsHistoryOnly ? MatchRank.NoMatch : c.Match,
            IsHistoryOnly: c.IsHistoryOnly,
            IsFavorite: c.IsFavorite,
            Priority: verdict.Effect,
            AlwaysTop: verdict.AlwaysTop,
            HistoryScoreX100: historyScore,
            TypeRank: c.TypeRank,
            IsDir: c.Result.IsDir,
            Name: c.Result.Name,
            NormalizedPath: path,
            Drive: RankPolicy.DriveRank(c.Result.Drive));
    }
}
