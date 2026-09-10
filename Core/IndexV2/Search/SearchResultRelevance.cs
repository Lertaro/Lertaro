using Lertaro.Core.SearchIndex;
using Lertaro.Core.SearchIndex.Fzf;

namespace Lertaro.Core.IndexV2.Search;

// Builds the top 32 bits of RankSortKey for a name-mode result -- the part SearchResultRankComparer
// compares first (after history priority). Kept separate from NameSearch so the two search phases cannot
// quietly develop different sort-key semantics again.
//
// Three keys, in this order (matching the quick window's RankAndDedupe exactly):
//   1. match START position -- "left-side match priority": the leftmost match wins. A row whose name did
//      not satisfy the whole query (it was completed by an ancestor folder) gets the "no name match"
//      sentinel, so every name match outranks it.
//   2. match QUALITY weight (HighlightMask: coverage * contiguity).
//   3. match TIER -- how the match was found (literal name, initials alias, full transliteration; see
//      AliasMatchRules). This is the WEAKEST of the three: it only separates rows that already agree on
//      both position and coverage, so 英文 > 简拼 > 全拼 never overrides a better-placed or tighter match.
// Start above weight matters because the weight's coverage share structurally favours shorter names:
// ranking "wx" by weight alone put "iwxfe.mp" (2/8) above "wxfef.doc" (2/9) even though the latter starts
// the name. The engine's pre-existing span/length bits are kept as deeper tie-breakers in the low 32.
internal static class SearchResultRelevance
{
    // Packing of the high 32 bits, all "smaller is better": start(8) | weight(16) | tier(8), i.e. shifted
    // by 56, 40 and 32. Start is narrowed to 8 bits because a Windows path component cannot exceed 255
    // characters; keeping the weight at its full 16-bit resolution preserves the finer-grained ordering
    // ahead of the tier, which is exactly the order requested (position, then coverage, then tier).
    private const int StartCap = 0xFF;

    public static void Apply(SearchResult result, FzfPattern pattern)
    {
        if (pattern.IsEmpty || string.IsNullOrEmpty(result.Path))
            return;

        var rank = FuzzyMatcher.IsMatch(pattern, result.Name)
            ? HighlightMask.ComputeRank(result.Name, pattern)
            : MatchRank.NoMatch;

        // An ancestor-completed row has no name to rank by; whole-path coverage is the only relevance
        // signal it has, and its sentinel start keeps it below every genuine name match.
        var weight = rank.IsMatch ? rank.Weight : HighlightMask.ComputeWeight(result.Path, pattern);
        var tier = rank.IsMatch ? rank.Tier : MatchRank.TierFull;
        var start = rank.IsMatch ? Math.Min(rank.Start, StartCap) : StartCap;
        var weightPoint = (ushort)Math.Round((1 - weight) * ushort.MaxValue);

        // The engine's own span/length tie-breakers stay in the low 32 bits, below all three keys.
        var low = result.RankSortKey & 0x0000_0000_FFFF_FFFFUL;
        result.RankSortKey = ((ulong)(byte)start << 56) | ((ulong)weightPoint << 40) | ((ulong)(byte)tier << 32) | low;
    }
}
