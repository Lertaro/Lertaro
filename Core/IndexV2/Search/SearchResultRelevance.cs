using Lertaro.Core.SearchIndex;
using Lertaro.Core.SearchIndex.Fzf;

namespace Lertaro.Core.IndexV2.Search;

// Builds the top 32 bits of RankSortKey for a name-mode result -- the part SearchResultRankComparer
// compares first (after history priority). Shared by NameSearch's candidate cut (HighKey) and the final
// per-result key (Apply) so the two search phases cannot quietly develop different sort-key semantics
// again.
//
// Keys, in this order (matching the quick window's RankAndDedupe exactly):
//   1. match SHAPE -- whole word > start of a word > inside a word > scattered letters (MatchShape). A
//      row whose name did not satisfy the whole query (it was completed by an ancestor folder) gets
//      ShapeNoName, so every name match outranks it.
//   2. RECENCY band -- created or changed today > this week > this month > older (RankSignals).
//   3. path DEPTH -- shallower first, so a project folder beats the same word deep in a game or package
//      tree (RankSignals).
//   4. match START position -- "left-side match priority".
//   5. match QUALITY weight (HighlightMask: coverage * contiguity).
//   6. match TIER -- how the match was found (literal name, initials alias, full transliteration; see
//      AliasMatchRules), so 英文 > 简拼 > 全拼 never overrides a better-shaped, better-placed or tighter match.
//   7. FILES before folders, only when everything above is equal.
// Shape above everything is what keeps "code-atlas" (whole word at 5) above "AutofillStates" (a-t-l-a-s
// scattered from 0) for "atlas", and keeps a Chrome profile file that changes every minute from riding
// recency past a real word match. Start above weight still matters, because the weight's coverage share
// structurally favours shorter names: ranking "wx" by weight alone put "iwxfe.mp" (2/8) above
// "wxfef.doc" (2/9) even though the latter starts the name. The engine's pre-existing span/length bits are
// kept as deeper tie-breakers in the low 32.
internal static class SearchResultRelevance
{
    // Packing of the high 32 bits, all "smaller is better":
    //   shape(3) | band(2) | depth(5) | start(7) | weight(10) | tier(4) | folder(1).
    // Depth stops at 31 folders and start at 127 characters -- past either, rows tie and fall to the keys
    // below. The weight keeps 1/1023 resolution, which separates every name length a real path holds
    // apart except near-identical long ones.
    private const int DepthCap = 0x1F;
    private const int StartCap = 0x7F;
    private const int WeightMax = 0x3FF;

    public static void Apply(SearchResult result, FzfPattern pattern)
    {
        if (pattern.IsEmpty || string.IsNullOrEmpty(result.Path))
            return;

        var rank = FuzzyMatcher.IsMatch(pattern, result.Name)
            ? HighlightMask.ComputeRank(result.Name, pattern)
            : MatchRank.NoMatch;

        // An ancestor-completed row has no name to rank by; whole-path coverage is the only relevance
        // signal it has, and its NoName shape keeps it below every genuine name match.
        if (!rank.IsMatch)
            rank = new MatchRank(MatchRank.TierFull, StartCap, HighlightMask.ComputeWeight(result.Path, pattern), MatchRank.ShapeNoName);

        var band = RankSignals.RecencyBand(result.Metadata.Created, result.Metadata.Modified, DateTime.UtcNow);

        // The engine's own span/length tie-breakers stay in the low 32 bits, below every key above.
        var low = result.RankSortKey & 0x0000_0000_FFFF_FFFFUL;
        result.RankSortKey = ((ulong)HighKey(rank, result.IsDir, band, RankSignals.PathDepth(result.Path)) << 32) | low;
    }

    public static uint HighKey(MatchRank rank, bool isDir, int band, int depth)
    {
        var shape = (uint)Math.Clamp(rank.Shape, 0, MatchRank.ShapeNoName);
        var recency = (uint)Math.Clamp(band, 0, RankSignals.BandOlder);
        var folders = (uint)Math.Clamp(depth, 0, DepthCap);
        var start = (uint)Math.Clamp(rank.Start, 0, StartCap);
        var weightPoint = (uint)Math.Round((1 - Math.Clamp(rank.Weight, 0, 1)) * WeightMax);
        var tier = (uint)Math.Clamp(rank.Tier, 0, 0xF);
        return (shape << 29) | (recency << 27) | (folders << 22) | (start << 15) | (weightPoint << 5) | (tier << 1) | (isDir ? 1u : 0u);
    }
}
