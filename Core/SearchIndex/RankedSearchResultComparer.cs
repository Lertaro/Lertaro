namespace Lertaro.Core.SearchIndex;

/// <summary>
/// Full-window V3 ordering over pre-decorated <see cref="SearchResult"/> rows. Mirrors
/// <see cref="RankKeyComparer"/> exactly (with <c>TypeRank = 0</c> and <c>IsHistoryOnly = false</c>) so the
/// quick and full windows preserve pairwise order across shared candidates.
/// </summary>
public sealed class RankedSearchResultComparer : IComparer<SearchResult>
{
    public static readonly RankedSearchResultComparer Instance = new();

    public int Compare(SearchResult? x, SearchResult? y)
    {
        if (ReferenceEquals(x, y))
            return 0;
        if (x == null)
            return -1;
        if (y == null)
            return 1;

        var sectionX = SectionOf(x);
        var sectionY = SectionOf(y);
        var c = ((byte)sectionX).CompareTo((byte)sectionY);
        if (c != 0)
            return c;

        var gradeX = SearchResultRanking.GradeOf(x);
        var gradeY = SearchResultRanking.GradeOf(y);
        var tierX = (HistoryTier)((x.RankFlags >> 3) & 3);
        var tierY = (HistoryTier)((y.RankFlags >> 3) & 3);
        var tieX = RankPolicy.HistoryTie(isHistoryOnly: false, tierX, x.HistoryScoreX100, gradeX);
        var tieY = RankPolicy.HistoryTie(isHistoryOnly: false, tierY, y.HistoryScoreX100, gradeY);
        var prioX = PriorityOrderOf(x);
        var prioY = PriorityOrderOf(y);

        switch (sectionX)
        {
            case RankSection.Pinned:
                var pinX = (x.RankScore & SearchResultRanking.FlagAlwaysTop) != 0 ? 1 : 0;
                var pinY = (y.RankScore & SearchResultRanking.FlagAlwaysTop) != 0 ? 1 : 0;
                c = pinY.CompareTo(pinX);
                if (c != 0)
                    return c;
                var favPinX = (x.RankScore & SearchResultRanking.FlagFavoritePin) != 0 ? 1 : 0;
                var favPinY = (y.RankScore & SearchResultRanking.FlagFavoritePin) != 0 ? 1 : 0;
                c = favPinY.CompareTo(favPinX);
                if (c != 0)
                    return c;
                c = gradeX.CompareTo(gradeY);
                if (c != 0)
                    return c;
                c = prioX.CompareTo(prioY);
                if (c != 0)
                    return c;
                c = tierY.CompareTo(tierX);
                if (c != 0)
                    return c;
                c = tieY.CompareTo(tieX);
                if (c != 0)
                    return c;
                break;

            case RankSection.Low:
                c = gradeX.CompareTo(gradeY);
                if (c != 0)
                    return c;
                c = tierY.CompareTo(tierX);
                if (c != 0)
                    return c;
                c = tieY.CompareTo(tieX);
                if (c != 0)
                    return c;
                break;

            default:
                c = gradeX.CompareTo(gradeY);
                if (c != 0)
                    return c;
                var favX = (x.RankScore & SearchResultRanking.FlagFavorite) != 0;
                var favY = (y.RankScore & SearchResultRanking.FlagFavorite) != 0;
                if (favX != favY)
                    return favX ? -1 : 1;
                c = prioX.CompareTo(prioY);
                if (c != 0)
                    return c;
                c = tierY.CompareTo(tierX);
                if (c != 0)
                    return c;
                c = tieY.CompareTo(tieX);
                if (c != 0)
                    return c;
                break;
        }

        // Packed quality, best first ("smaller is better" in high 32 bits; 0 means unpacked/worst).
        var keyX = x.RankSortKey == 0 ? uint.MaxValue : (uint)(x.RankSortKey >> 32);
        var keyY = y.RankSortKey == 0 ? uint.MaxValue : (uint)(y.RankSortKey >> 32);
        c = keyX.CompareTo(keyY);
        if (c != 0)
            return c;

        return RankPolicy.CompareCommonTail(TailOf(x), TailOf(y));
    }

    private static RankSection SectionOf(SearchResult result) =>
        result.RankFlags == 0 && result.RankScore == 0
            ? RankSection.Boosted
            : (RankSection)(result.RankFlags & 3);

    private static int PriorityOrderOf(SearchResult result) =>
        result.RankFlags == 0 && result.RankScore == 0
            ? RankPolicy.PriorityOrder(IndexRuleEffect.Normal)
            : (result.RankScore >> SearchResultRanking.PriorityShift) & 3;

    private static RankTail TailOf(SearchResult result)
    {
        var path = result.Path.Length > 3 && result.Path[^1] == '\\'
            ? result.Path.TrimEnd('\\')
            : result.Path;

        return new RankTail(
            result.IsDir,
            result.Name,
            path,
            RankPolicy.DriveRank(result.Drive),
            result.RankSortKey & 0xFFFF_FFFFUL);
    }
}
