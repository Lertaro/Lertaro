using Lertaro.Core.SearchIndex;
using Lertaro.Core.SearchIndex.Fzf;

namespace Lertaro.Core.IndexV2.Search;

// Builds the top 32 bits of RankSortKey for a name-mode result -- the part SearchResultRankComparer
// compares first. Shared by NameSearch's candidate cut (HighKey) and the final per-result key (Apply) so
// the two search phases cannot quietly develop different sort-key semantics again.
//
// This is the SAME quality sequence MatchRank.CompareQuality gives the quick window:
//   contiguity -> shape -> left-contiguous (tie break) -> start -> coverage -> tier.
// History is deliberately NOT in this key: both windows compare it after the match keys, as a band
// derived from how often the user opened the path (see SearchResultRankComparer and
// RankedCandidateOrdering). Recency band and path depth are not ranking keys.
internal static class SearchResultRelevance
{
    // Packing of the high 32 bits, all "smaller is better":
    //   contiguity(8) | shape(3) | not-left-contiguous(1) | start(7) | coverage(9) | tier(4)
    // Start is 7 bits because MatchRank.StartCap is 127. Left-contiguous is only a shape tie-breaker,
    // and is stored as "not left" so the better value stays zero. Contiguity keeps 8-bit resolution
    // (1/255); coverage keeps 9 (1/511).
    private const int ContiguityMax = 0xFF;
    private const int CoverageMax = 0x1FF;

    public static void Apply(SearchResult result, FzfPattern pattern)
    {
        if (pattern.IsEmpty || string.IsNullOrEmpty(result.Path))
            return;

        var rank = FuzzyMatcher.ComputeMatchRank(result.Name, pattern);

        // An ancestor-completed row has no name to rank by; whole-path coverage is the only relevance
        // signal it has. MatchRank.NoNameFallback gives it the same rank in both windows. A
        // positive-less query (only inverse terms) is not this case: ComputeMatchRank returns the
        // synthetic NoPositiveTerms rank, which both windows compare identically.
        if (!rank.IsMatch)
            rank = MatchRank.NoNameFallback(HighlightMask.ComputeWeight(result.Path, pattern));

        // The engine's own span/length tie-breakers stay in the low 32 bits, below every key above.
        var low = result.RankSortKey & 0x0000_0000_FFFF_FFFFUL;
        result.RankSortKey = ((ulong)HighKey(rank) << 32) | low;
    }

    public static uint HighKey(in MatchRank rank)
    {
        var notLeftContiguous = rank.IsLeftContiguous ? 0u : 1u;
        var contiguityPoint = (uint)Math.Round((1 - Clamp01(rank.Contiguity)) * ContiguityMax);
        var shape = (uint)Math.Clamp(rank.Shape, 0, MatchRank.ShapeNoName);
        var start = (uint)Math.Clamp(rank.Start, 0, MatchRank.StartCap);
        var coveragePoint = (uint)Math.Round((1 - Clamp01(rank.Coverage)) * CoverageMax);
        var tier = (uint)Math.Clamp(rank.Tier, 0, 0xF);
        return (contiguityPoint << 24) | (shape << 21) | (notLeftContiguous << 20) | (start << 13) | (coveragePoint << 4) | tier;
    }

    /// <summary>True when the packed quality key describes a strong match. Mirrors the layout in
    /// <see cref="HighKey"/>: tier in bits 0-3, shape in bits 21-23 of the high word. A key of 0 (never
    /// packed) is NOT handled here -- the caller decides, because an unpacked key must not read as the
    /// best possible tier and shape.</summary>
    public static bool IsStrongMatch(ulong rankSortKey) =>
        MatchRank.IsStrongHit(
            tier: (int)((rankSortKey >> 32) & 0xF),
            shape: (int)((rankSortKey >> 53) & 0x7));

    private static double Clamp01(double value)
    {
        if (double.IsNaN(value))
            return 0;
        return Math.Clamp(value, 0, 1);
    }
}
