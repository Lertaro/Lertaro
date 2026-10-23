namespace Lertaro.Core.SearchIndex;

/// <summary>
/// Engine truncation order: match quality, then the shared path tail. Personal ranking signals are not
/// available at this stage, so this comparer is intentionally separate from final ranking.
/// </summary>
public sealed class SearchCandidateComparer : IComparer<SearchResult>
{
    public static readonly SearchCandidateComparer Instance = new();

    public int Compare(SearchResult? x, SearchResult? y)
    {
        if (ReferenceEquals(x, y))
            return 0;
        if (x == null)
            return -1;
        if (y == null)
            return 1;

        // Smaller packed key is a better match, so this is ascending, as it has always been here.
        var compare = ((uint)(x.RankSortKey >> 32)).CompareTo((uint)(y.RankSortKey >> 32));
        if (compare != 0)
            return compare;

        return RankPolicy.CompareCommonTail(TailOf(x), TailOf(y));
    }

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
