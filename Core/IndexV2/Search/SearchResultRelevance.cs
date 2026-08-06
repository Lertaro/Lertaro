using Lertaro.Core.SearchIndex;
using Lertaro.Core.SearchIndex.Fzf;

namespace Lertaro.Core.IndexV2.Search;

// Applies the same whole-path relevance measure to ordinary name matches and to matches completed by
// an ancestor folder. Kept separate from NameSearch so the two search phases cannot quietly develop
// different sort-key semantics again.
internal static class SearchResultRelevance
{
    // A full filename match is stronger evidence than finding part of the query only in an ancestor,
    // but it remains a bounded bonus: an especially compact complete path match can still outrank a
    // poor scattered filename match.
    private const double FullNameMatchBonus = 0.08;

    public static void Apply(SearchResult result, FzfPattern pattern)
    {
        if (pattern.IsEmpty || string.IsNullOrEmpty(result.Path))
            return;

        var relevance = HighlightMask.ComputeWeight(result.Path, pattern);
        if (pattern.TryMatch(result.Name, out _, FzfScoringScheme.Default))
            relevance = Math.Min(1, relevance + FullNameMatchBonus);

        var scorePoint = (ushort)Math.Round((1 - relevance) * ushort.MaxValue);
        result.RankSortKey = (result.RankSortKey & 0x0000FFFFFFFFFFFFUL) | ((ulong)scorePoint << 48);
    }
}
