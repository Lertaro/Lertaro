using Lertaro.Core.IndexV2.Search;
using PriorityVerdict = Lertaro.Core.ExclusionRuleSet.PriorityVerdict;

namespace Lertaro.Core.SearchIndex;

/// <summary>
/// Decorates full-window <see cref="SearchResult"/> rows once so <see cref="RankedSearchResultComparer"/>
/// compares the exact V3 tuple without repeating rule or history lookups per comparison.
/// </summary>
/// <remarks>
/// Bit layout of <see cref="SearchResult.RankFlags"/>:
///   bits 0..1  Section (0 Pinned, 1 Boosted, 2 Low, 3 HistoryTail)
///   bit  2     Strong match flag (1 = preference-eligible Grade 0..1)
///   bits 3..4  HistoryTier (0 None, 1 Tail, 2 H3, 3 H5)
///   bits 5..7  MatchEvidenceGrade (1 ExactIdentity .. 4 SecondaryField)
///
/// Bit layout of <see cref="SearchResult.RankScore"/>:
///   holds <see cref="RankPolicy.DiagnosticScore"/> plus packed V3 sub-keys in low bits for zero-allocation
///   comparison (AlwaysTop, FavoritePin, IsFavorite, PriorityOrder) inside <see cref="RankedSearchResultComparer"/>.
/// </remarks>
public static class SearchResultRanking
{
    internal const int FlagAlwaysTop = 1 << 16;
    internal const int FlagFavoritePin = 1 << 17;
    internal const int FlagFavorite = 1 << 18;
    internal const int PriorityShift = 19;
    internal const int DiagnosticScoreMask = 0xFFFF;

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

        var grade = ResolveFullWindowGrade(result, context?.Query);
        if (result.IsDir && grade == MatchEvidenceGrade.ExactIdentity)
            grade = MatchEvidenceGrade.DirectStrong;
        var isStrong = RankPolicy.IsPreferenceEligible(grade);

        var rawTier = !isStrong ? HistoryTier.None
            : scoreX100 >= RankPolicy.HistoryFiveThresholdX100 ? HistoryTier.H5
            : scoreX100 >= RankPolicy.HistoryThreeThresholdX100 ? HistoryTier.H3
            : HistoryTier.None;
        var tier = RankPolicy.EffectiveHistoryTier(rawTier, grade, isHistoryOnly: false);

        var isFavorite = context?.FavoritePaths?.Contains(path) == true;
        var explicitAlwaysTop = verdict.AlwaysTop && verdict.Effect == IndexRuleEffect.High;
        var favoritePin = !explicitAlwaysTop && isFavorite && isStrong;
        var section = RankPolicy.Section(verdict.Effect, explicitAlwaysTop, favoritePin, isHistoryOnly: false, grade);
        var diagScore = RankPolicy.DiagnosticScore(section, explicitAlwaysTop, favoritePin, grade, verdict.Effect, tier);

        result.HistoryScoreX100 = scoreX100;
        result.RankFlags = (byte)(
            ((int)section & 3)
            | (isStrong ? 4 : 0)
            | (((int)tier & 3) << 3)
            | (((int)grade & 7) << 5));
        result.RankScore = (diagScore & DiagnosticScoreMask)
            | (explicitAlwaysTop ? FlagAlwaysTop : 0)
            | (favoritePin ? FlagFavoritePin : 0)
            | (isFavorite ? FlagFavorite : 0)
            | ((RankPolicy.PriorityOrder(verdict.Effect) & 3) << PriorityShift);
    }

    internal static int DiagnosticScoreOf(SearchResult result) =>
        result.RankScore & DiagnosticScoreMask;

    internal static MatchEvidenceGrade GradeOf(SearchResult result)
    {
        var raw = (MatchEvidenceGrade)((result.RankFlags >> 5) & 7);
        return raw != MatchEvidenceGrade.LegacyUnknown
            ? raw
            : (result.RankFlags & 4) != 0
                ? MatchEvidenceGrade.DirectStrong
                : MatchEvidenceGrade.DirectApproximate;
    }

    private static MatchEvidenceGrade ResolveFullWindowGrade(SearchResult result, string? query)
    {
        if (result.RankSortKey == 0)
            return MatchEvidenceGrade.DirectApproximate;

        var high = (uint)(result.RankSortKey >> 32);
        var shape = (int)((high >> 21) & 0x7);
        if (shape == MatchRank.ShapeNoName)
            return MatchEvidenceGrade.SecondaryField;

        var isStrong = SearchResultRelevance.IsStrongMatch(result.RankSortKey);
        if (!isStrong)
            return MatchEvidenceGrade.DirectApproximate;

        if (!string.IsNullOrWhiteSpace(query))
        {
            var notLeft = (int)((high >> 20) & 0x1);
            var start = (int)((high >> 13) & 0x7F);
            var coverageInv = (int)((high >> 4) & 0x1FF);
            var tier = (int)(high & 0xF);
            var approxRank = new MatchRank(tier, start, 1d - coverageInv / 511d, notLeft == 0 ? 1d : 0.5d, shape);
            // The variant stays Original: the packed key says how well the name matched, never whether a
            // joined reading walk or one term per syllable run produced it, and claiming the joined witness
            // would hand Grade 0 to a swapped-order hit ("sousuo jieguo" on "结果搜索") that the quick window
            // refuses. A single-term reading still earns ExactIdentity, which is the case with no order to
            // get wrong; a spaced query keeps the grade the quick window's failed joined branch gives it.
            if (MatchEvidence.IsExactIdentityOrWholeStemReading(
                    query,
                    result.Name,
                    MatchEvidenceField.FileName,
                    approxRank,
                    MatchQueryVariant.Original,
                    isDirectory: result.IsDir))
            {
                return MatchEvidenceGrade.ExactIdentity;
            }
        }

        return MatchEvidenceGrade.DirectStrong;
    }
}
