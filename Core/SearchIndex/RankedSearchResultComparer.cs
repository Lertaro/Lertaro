namespace Lertaro.Core.SearchIndex;

/// <summary>
/// The full window's ordering, the equivalent of <see cref="RankKeyComparer"/> over rows that carry their
/// keys in fields instead of a copied key array. The key order is deliberately the same one the quick
/// window uses, and both read it from <see cref="RankPolicy.CompareSectionRank"/> and
/// <see cref="RankPolicy.CompareCommonTail"/>, so one query cannot produce two file orders.
/// </summary>
/// <remarks>
/// Two differences follow from what this window has: no display type layer (TypeRank is always 0), and no
/// history-tail rows, because the window adds no history rows of its own -- history only lifts a row it
/// found, and only through the threshold tier <see cref="SearchResultRanking"/> wrote.
/// Reads only decorated fields, so a row that was never decorated compares as a weak normal-priority row.
/// </remarks>
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

        var sectionX = (RankSection)(x.RankFlags & 3);
        var sectionY = (RankSection)(y.RankFlags & 3);
        var compare = ((byte)sectionX).CompareTo((byte)sectionY);
        if (compare != 0)
            return compare;

        // One rule for the tier->tie decision, shared with the quick window's kernel: this window has no
        // history-only rows, so isHistoryOnly is false on both sides.
        var tierX = (HistoryTier)((x.RankFlags >> 3) & 3);
        var tierY = (HistoryTier)((y.RankFlags >> 3) & 3);
        var rankX = new SectionRank(
            x.RankScore,
            RankPolicy.HistoryTie(isHistoryOnly: false, tierX, x.HistoryScoreX100),
            0);
        var rankY = new SectionRank(
            y.RankScore,
            RankPolicy.HistoryTie(isHistoryOnly: false, tierY, y.HistoryScoreX100),
            0);

        compare = RankPolicy.CompareSectionRank(sectionX, in rankX, in rankY);
        if (compare != 0)
            return compare;

        // Packed quality, best first. Every field of the high 32 bits is stored as "smaller is better"
        // (see SearchResultRelevance.HighKey), so the ascending comparison the window already used is the
        // one that keeps a better match on top -- inverting it here would put the worst rows first.
        compare = ((uint)(x.RankSortKey >> 32)).CompareTo((uint)(y.RankSortKey >> 32));
        if (compare != 0)
            return compare;

        return RankPolicy.CompareCommonTail(TailOf(x), TailOf(y));
    }

    // Trailing separator trimmed, exactly as the window's own comparer has always done: folder rows arrive
    // spelled both ways, and a tie that depends on which one a caller happened to use is not an order.
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
