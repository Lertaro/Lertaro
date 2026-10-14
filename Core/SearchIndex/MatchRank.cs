namespace Lertaro.Core.SearchIndex;

// One candidate's match quality, as the search windows rank it. Shape says how the query sits in the
// text (whole word, start of a word, inside a word, scattered letters -- see MatchShape), Start is the
// index of the leftmost matched character, Weight is the coverage-and-contiguity measure (HighlightMask),
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
public readonly record struct MatchRank(int Tier, int Start, double Weight, int Shape = MatchRank.ShapeWholeWord)
{
    // How the match was found, best first. A candidate's own name is the strongest evidence that the
    // query really describes it; an initials/shorthand alias means the user abbreviated; a full
    // transliteration means the whole reading had to be spelled out.
    public const int TierName = 0;
    public const int TierInitials = 1;
    public const int TierFull = 2;

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
    public static MatchRank NoMatch => new(int.MaxValue, int.MaxValue, 0, ShapeNoName);

    public bool IsMatch => Start != int.MaxValue;
}
