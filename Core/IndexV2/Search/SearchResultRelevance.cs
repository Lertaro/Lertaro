using Lertaro.Core.SearchIndex;
using Lertaro.Core.SearchIndex.Fzf;

namespace Lertaro.Core.IndexV2.Search;

// Builds the top 32 bits of RankSortKey for a name-mode result -- the part SearchResultRankComparer
// compares first (after history priority). Shared by NameSearch's candidate cut (HighKey) and the final
// per-result key (Apply) so the two search phases cannot quietly develop different sort-key semantics
// again.
//
// Keys, in this order (matching the quick window's RankAndDedupe exactly):
//   1. match START position -- "left-side match priority": the row whose match starts furthest left in its
//      name wins. This leads because with a result cap, any signal placed above it fills the list with rows
//      that merely matched a whole word or were touched today, and the row the user wants is cut off.
//   2. match SHAPE -- whole word > start of a word > inside a word > scattered letters (MatchShape). A
//      row whose name did not satisfy the whole query (it was completed by an ancestor folder) gets
//      ShapeNoName, so every name match outranks it.
//   3. RECENCY band -- created or changed today > this week > this month > older (RankSignals).
//   4. path DEPTH -- shallower first, so a project folder beats the same word deep in a game or package
//      tree (RankSignals).
//   5. match QUALITY weight (HighlightMask: coverage * contiguity).
//   6. match TIER -- how the match was found (literal name, initials alias, full transliteration; see
//      AliasMatchRules), so 英文 > 简拼 > 全拼 never overrides a better-placed or tighter match.
//   7. FILES before folders, only when everything above is equal.
// Shape, recency and depth still matter, but only among rows that start at the same position: they are what
// keeps "code-atlas" (whole word at 5) above "AutofillStates" (a-t-l-a-s scattered from 0) for "atlas", and
// what keeps a Chrome profile file that changes every minute from riding recency past a real word match.
internal static class SearchResultRelevance
{
    // Packing of the high 32 bits, all "smaller is better":
    //   shape(3) | band(2) | depth(5) | start(7) | weight(10) | tier(4) | folder(1).
    // Depth stops at 31 folders and start at 127 characters -- past either, rows tie and fall to the keys
    // below. The weight keeps 1/1023 resolution, which separates every name length a real path holds
    // apart except near-identical long ones.
    private const int DepthCap = 0x1F;
    private const int WeightMax = 0x3FF;

    public static void Apply(SearchResult result, FzfPattern pattern)
    {
        if (pattern.IsEmpty || string.IsNullOrEmpty(result.Path))
            return;

        var rank = FuzzyMatcher.IsMatch(pattern, result.Name)
            ? HighlightMask.ComputeRank(result.Name, pattern)
            : MatchRank.NoMatch;

        // An ancestor-completed row has no name to rank by; whole-path coverage is the only relevance
        // signal it has, and its NoName shape keeps it below every genuine name match. The rank itself is
        // MatchRank.NoNameFallback's, shared with the quick window's own ladder so both windows order these
        // rows identically.
        if (!rank.IsMatch)
            rank = MatchRank.NoNameFallback(HighlightMask.ComputeWeight(result.Path, pattern));

        var band = RankSignals.RecencyBand(result.Metadata.Created, result.Metadata.Modified, DateTime.UtcNow);

        // The engine's own span/length tie-breakers stay in the low 32 bits, below every key above.
        var low = result.RankSortKey & 0x0000_0000_FFFF_FFFFUL;
        result.RankSortKey = ((ulong)HighKey(rank, result.IsDir, band, RankSignals.PathDepth(result.Path), IsExactName(result.Name, pattern)) << 32) | low;
    }

    /// <summary>
    /// Whether the name IS the query (extension ignored). The streamed list is ordered by this key, so without
    /// it a history row that happened to arrive first outranked a file literally named after the query.
    /// </summary>
    public static bool IsExactName(string name, FzfPattern pattern)
    {
        if (pattern.OrGroups != null || pattern.TermSets.Length != 1)
            return false;
        var terms = pattern.TermSets[0].Terms;
        if (terms.Length != 1 || terms[0].Inverse || terms[0].AliasForm)
            return false;
        var query = terms[0].Text;
        if (query.Length == 0)
            return false;
        var stem = Path.GetFileNameWithoutExtension(name);
        return string.Equals(stem, query, StringComparison.OrdinalIgnoreCase);
    }
    public static uint HighKey(MatchRank rank, bool isDir, int band, int depth, bool exactName = false)
    {
        var shape = (uint)Math.Clamp(rank.Shape, 0, MatchRank.ShapeNoName);
        var recency = (uint)Math.Clamp(band, 0, RankSignals.BandOlder);
        var folders = (uint)Math.Clamp(depth, 0, DepthCap);
        var start = (uint)Math.Clamp(rank.Start, 0, 63);
        var weightPoint = (uint)Math.Round((1 - Math.Clamp(rank.Weight, 0, 1)) * WeightMax);
        var tier = (uint)Math.Clamp(rank.Tier, 0, 0xF);
        // "Left-side match priority" leads, same as the App-side ladder (RankedCandidateOrdering): the row whose
        // match starts furthest left is the one the user is looking for, and with a result cap any signal placed
        // above it fills the list with rows that merely matched a whole word or were touched today. Shape, recency
        // and depth keep their weight as tiebreakers among rows that start at the same position.
        //   start(7) | shape(3) | band(2) | depth(5) | weight(10) | tier(4) | folder(1)
        // Owner's rule: leftmost, then how contiguous the hit is, then coverage, then how the match was found;
        // shape/recency/depth no longer take part (they were what buried the row the user was looking for).
        //   start(7) | contiguity(10) | weight(10) | tier(4) | folder(1)
        var contiguityPoint = (uint)Math.Round((1 - Math.Clamp(rank.Contiguity, 0, 1)) * 1023);
        // Matched length, not the coverage share: coverage favours short names, so a file called "aa" beat every
        // longer name that merely started with the same letter.
        var lengthPoint = (uint)(1023 - Math.Clamp(rank.MatchedLength, 0, 1023));
        return (exactName ? 1u : 0u) << 31 | (start << 25) | (contiguityPoint << 15) | (weightPoint << 5) | (tier << 1) | (isDir ? 1u : 0u);
    }
}
