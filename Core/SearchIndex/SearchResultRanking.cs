using Lertaro.Core.IndexV2.Search;
using PriorityVerdict = Lertaro.Core.ExclusionRuleSet.PriorityVerdict;

namespace Lertaro.Core.SearchIndex;

/// <summary>
/// Stores ranking fields once so the full-window comparer performs no rule or history lookup. Decoration is
/// idempotent. History uses fixed thresholds because streaming rows cannot share a stable page budget.
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
