namespace Lertaro.Core.SearchIndex;

// One candidate's match quality, as both search windows rank it. There is exactly one key order:
//
//   Contiguity (how many matched terms are one run: a multi-term query gets one run per term)
//   -> Shape (whole word > word start > inside word > scattered)
//   -> left-contiguous (Start == 0, only to break a shape tie)
//   -> Start (leftmost matched character)
//   -> Coverage (what share of the name the match explains)
//   -> Tier (literal name > initials alias > full transliteration > rare reading)
//
// Shape deliberately outranks left-contiguity: "code-atlas" is the whole word the user typed, while
// "atlasing0" merely starts with it. When both hits have the same shape, the left edge still wins. This
// is the owner's 2026-10-19 choice; the earlier "left-contiguous first" rule survives only inside a
// shape tie.
//
// The full window packs the same keys into SearchResultRelevance.HighKey; the quick window compares them
// through CompareQuality. History/favorites are compared after these keys by both windows.
public readonly record struct MatchRank(
    int Tier,
    int Start,
    double Coverage,
    double Contiguity = 1,
    int Shape = MatchRank.ShapeWholeWord)
{
    // How the match was found, best first. A candidate's own name is the strongest evidence that the
    // query really describes it; an initials/shorthand alias means the user abbreviated; a full
    // transliteration means the whole reading had to be spelled out.
    public const int TierName = 0;
    public const int TierInitials = 1;
    public const int TierFull = 2;
    // A pinyin match that had to use a character's less common reading (see PinyinNameEvaluator):
    // below every common-reading match, above nothing else.
    public const int TierFullRare = 3;

    // How the query sits in the text, best first. Alias hits rank as WordStart: providers only accept
    // matches aligned to a syllable/word start (AliasMatchRules.IsBoundaryAligned). NoName is the shape
    // of a row whose own name did not match at all (an ancestor folder completed the query).
    public const int ShapeWholeWord = 0;
    public const int ShapeWordStart = 1;
    public const int ShapeInsideWord = 2;
    public const int ShapeScattered = 3;
    public const int ShapeNoName = 4;

    // Start == int.MaxValue marks "this text did not match the whole query" -- distinct from a match at
    // index 0, which is the best possible Start.
    public static MatchRank NoMatch => new(int.MaxValue, int.MaxValue, 0, 0, ShapeNoName);

    // A query made only of inverse terms has no positive text to paint; every candidate satisfies it.
    // Keep it at the bottom of the quality ladder so it cannot masquerade as a real name match.
    public static MatchRank NoPositiveTerms => new(TierName, 0, 0, 0, ShapeScattered);

    public bool IsMatch => Start != int.MaxValue;

    public bool IsLeftContiguous => Start == 0 && Contiguity >= 1;

    // Shape, rather than aggregate contiguity, defines strong matches: a clean multi-term hit can have
    // contiguity below 1. Scattered, no-name and rare-reading pinyin hits remain weak.
    public bool IsStrong => IsMatch && IsStrongHit(Tier, Shape);

    /// <summary>Shared strong-match rule: tier through full pinyin and shape through inside-word.</summary>
    public static bool IsStrongHit(int tier, int shape) => tier <= TierFull && shape <= ShapeInsideWord;

    // Kept as the single quality number some callers compare for diagnostics; ordering itself uses the
    // individual keys above so a short name cannot win merely because coverage is a ratio.
    public double Weight => Coverage * Contiguity;

    // The full window's row key packs Start into 7 bits, so 127 is its ceiling.
    internal const int StartCap = 0x7F;

    /// <summary>
    /// The rank of a row whose own NAME did not match the whole query: it was reached by an ancestor
    /// folder completing the query. Ranked by how much of the whole path the query explains.
    /// </summary>
    internal static MatchRank NoNameFallback(double pathCoverage)
        => new(TierFull, StartCap, pathCoverage, 0, ShapeNoName);

    /// <summary>
    /// The shared quality comparison, smallest/best first. Both windows use this exact sequence so they
    /// cannot order the same two rows differently.
    /// </summary>
    public static int CompareQuality(in MatchRank left, in MatchRank right)
    {
        var compare = right.Contiguity.CompareTo(left.Contiguity);
        if (compare != 0)
            return compare;

        compare = Math.Clamp(left.Shape, 0, ShapeNoName).CompareTo(Math.Clamp(right.Shape, 0, ShapeNoName));
        if (compare != 0)
            return compare;

        if (left.IsLeftContiguous != right.IsLeftContiguous)
            return left.IsLeftContiguous ? -1 : 1;

        var leftStart = Math.Min(left.Start, StartCap);
        var rightStart = Math.Min(right.Start, StartCap);
        compare = leftStart.CompareTo(rightStart);
        if (compare != 0)
            return compare;

        compare = right.Coverage.CompareTo(left.Coverage);
        if (compare != 0)
            return compare;

        return left.Tier.CompareTo(right.Tier);
    }
}
