namespace Lertaro.Core.SearchIndex;

/// <summary>
/// The key the engine truncates on: match quality, then the shared path tail. Deliberately not the final
/// ranking (redesign 5.4) -- when a search keeps only its best <c>limit</c> rows per drive it has no
/// history, favorite or priority context yet, and borrowing the final comparer for that step made the
/// truncation depend on rules it cannot see.
/// </summary>
/// <remarks>
/// Equivalent to what <see cref="SearchResultRankComparer"/> degrades to with an empty history, no
/// favorites and no priority resolver: every band that comparer adds collapses to a tie, so quality then
/// the path tail is all that is left. Kept as its own type so a change to the final ranking cannot quietly
/// change which rows survive truncation.
/// </remarks>
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
