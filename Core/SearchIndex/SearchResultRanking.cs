using Lertaro.Core.IndexV2.Search;
using PriorityVerdict = Lertaro.Core.ExclusionRuleSet.PriorityVerdict;

namespace Lertaro.Core.SearchIndex;

/// <summary>
/// Writes the ranking kernel's answer onto a <see cref="SearchResult"/> once, so the full window's comparer
/// only reads fields (redesign 5.3). A hundred thousand rows are decorated a single time and then compared
/// millions of times; deriving a rule verdict or a history lookup per comparison is what made that path
/// cost more than the search itself.
///
/// Decorating is idempotent: the same context applied twice leaves the same values, which is what lets
/// <c>StreamingResultAccumulator</c> re-decorate its seed rows when the context arrives later.
///
/// History tiers here are pure thresholds, NOT the H5-at-most-3 / H3-at-most-10 budget of redesign 3.4.
/// The full window merges each arriving batch into a list already on screen, so a row's section has to be
/// final when it arrives: recomputing a shared budget would move rows the user is looking at, and walking
/// a hundred thousand rows per batch is exactly the cost this decorator exists to avoid. The quick window
/// keeps the capped budget because it ranks one page at a time.
/// </summary>
public static class SearchResultRanking
{
    public static void Decorate(SearchResult result, RankContext? context)
    {
        var path = result.Path.Length > 3 && result.Path[^1] == '\\'
            ? result.Path.TrimEnd('\\')
            : result.Path;

        var verdict = context?.PriorityResolver?.Invoke(path, result.IsDir)
                      ?? new PriorityVerdict(IndexRuleEffect.Normal, false);

        var scoreX100 = context is not null && context.HistoryScoreX100.TryGetValue(path, out var known)
            ? known
            : 0;

        // A zero key means the row was never ranked (legacy RankSortKey 0 sorted as the WORST quality).
        // Treat it as weak so such a row cannot claim the strong-match score.
        var isStrong = result.RankSortKey != 0 && SearchResultRelevance.IsStrongMatch(result.RankSortKey);

        var tier = !isStrong ? HistoryTier.None
            : scoreX100 >= RankPolicy.HistoryFiveThresholdX100 ? HistoryTier.H5
            : scoreX100 >= RankPolicy.HistoryThreeThresholdX100 ? HistoryTier.H3
            : HistoryTier.None;

        var isFavorite = context?.FavoritePaths?.Contains(path) == true;

        // No history-only rows exist in this window, so Tail never appears and bit 1 of the section stays 0.
        var section = RankPolicy.Section(isStrong, isFavorite, isHistoryOnly: false, verdict.Effect, verdict.AlwaysTop, tier);

        result.RankScore = RankPolicy.Score(isStrong, isFavorite, isHistoryOnly: false, verdict.Effect, tier);
        result.HistoryScoreX100 = scoreX100;
        result.RankFlags = (byte)((int)section | (isStrong ? 4 : 0) | ((int)tier << 3));
    }
}
