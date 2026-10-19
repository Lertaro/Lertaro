namespace Lertaro.Core.SearchIndex;

/// <summary>The shared match-quality order used by both windows and by App-side candidate sorts.</summary>
public sealed class MatchRankQualityComparer : IComparer<MatchRank>
{
    public static readonly MatchRankQualityComparer Instance = new();

    public int Compare(MatchRank x, MatchRank y) => MatchRank.CompareQuality(x, y);
}
