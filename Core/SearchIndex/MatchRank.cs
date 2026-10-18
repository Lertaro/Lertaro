namespace Lertaro.Core.SearchIndex;

// One candidate's match quality, as the search windows rank it. Shape says how the query sits in the
// text (whole word, start of a word, inside a word, scattered letters -- see MatchShape), Start is the
// index of the leftmost matched character, Weight is the coverage-and-contiguity measure (HighlightMask),
// MatchedLength is how many characters the query actually consumed (an absolute count, so a long contiguous
// run beats a short name that merely covers a large share of itself),
// and Tier says how the match was found at all (see AliasMatchRules.TierFor).
//
// Rank order is Shape, then Start, then Weight, then Tier -- with every window slotting the row's recency
// band and path depth (RankSignals) in right after Shape, since they are facts about the row, not the
// match. Shape leads because a whole-word hit later in
// the name ("code-atlas" for "atlas") is what the user typed, and a scattered subsequence that merely
// starts at 0 ("AutofillStates") is not; start used to lead and let thousands of the latter bury it.
// How the match was found stays the WEAKEST key, so 英文 > 简拼 > 全拼 only separates rows that already
// agree on shape, position and tightness.
//
// The windows differ only in what they put ABOVE this: the quick/full windows use user settings (history,
// result-type order), and the inline window layers directory proximity on top (see DirectoryProximity).
// Contiguity is carried separately from Weight because the owner's ordering rule is "leftmost, then how
// contiguous the hit is, then how much of the name it covers": the packed weight is coverage*contiguity, so
// a long name with one solid run used to lose to a short name full of scattered letters.
public readonly record struct MatchRank(int Tier, int Start, double Weight, int Shape = MatchRank.ShapeWholeWord, double Contiguity = 0, int MatchedLength = 0)
{
    // How the match was found, best first. A candidate's own name is the strongest evidence that the
    // query really describes it; an initials/shorthand alias means the user abbreviated; a full
    // transliteration means the whole reading had to be spelled out.
    public const int TierName = 0;
    public const int TierInitials = 1;
    public const int TierFull = 2;
    // A pinyin match that had to use a character's less common reading (see PinyinNameEvaluator):
    // below every common-reading match, above nothing else. The reference demotes the same thing by
    // -12 in its score table; here it is the weakest separate key instead.
    public const int TierFullRare = 3;

    // How the query sits in the text, best first. Alias hits rank as WordStart: providers only accept
    // matches aligned to a syllable/word start (AliasMatchRules.IsBoundaryAligned). NoName is the shape of
    // a row whose own name did not match at all (an ancestor folder completed the query).
    public const int ShapeWholeWord = 0;
    public const int ShapeWordStart = 1;
    public const int ShapeInsideWord = 2;
    public const int ShapeScattered = 3;
    public const int ShapeNoName = 4;

    // Start == int.MaxValue marks "this text did not match the whole query" -- distinct from a match at
    // index 0, which is the best possible Start.
    public static MatchRank NoMatch => new(int.MaxValue, int.MaxValue, 0, ShapeNoName, 0, 0);

    public bool IsMatch => Start != int.MaxValue;

    // The full window's row key packs Start into 7 bits, so 127 is its ceiling. Shared here because the
    // quick window's own ladder has to use the same value for the one rank it does NOT derive itself (see
    // NoNameFallback): past the cap the packed key can no longer tell two starts apart, and a ladder that
    // kept the exact positions would order the same two rows the other way round.
    internal const int StartCap = 0x7F;

    /// <summary>
    /// The rank of a row whose own NAME did not match the whole query: it was reached through a
    /// transliteration of that name, or (in the full window) by an ancestor folder completing the query.
    /// Ranked by how much of the whole path the query explains, with the shape that marks "no name match".
    /// </summary>
    /// <remarks>
    /// Both windows must land on this same rank. The engine's row key already did (SearchResultRelevance
    /// .Apply), while the quick window used to leave such a row at NoMatch -- identical in shape but with
    /// Start/Weight/Tier left at their no-match values, so it fell back to ordering them by path length and
    /// name where the full window ordered the same rows by coverage. One query, two file orders. The App
    /// reaches this through FuzzyQuery.FallbackRank, which is the seam it is meant to use.
    /// </remarks>
    internal static MatchRank NoNameFallback(double pathWeight) => new(TierFull, StartCap, pathWeight, ShapeNoName);
}
