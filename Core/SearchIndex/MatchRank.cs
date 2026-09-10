namespace Lertaro.Core.SearchIndex;

// One candidate's match quality, as the two search windows rank it. Start is the index of the leftmost
// matched character ("left-side match priority" -- a match beginning earlier wins), Weight is the
// coverage-and-contiguity measure (HighlightMask), and Tier says how the match was found at all (see
// AliasMatchRules.TierFor).
//
// Rank order is Start, then Weight, then Tier: how the match was found is the WEAKEST key, so
// 英文 > 简拼 > 全拼 only separates rows that already agree on where and how tightly they matched.
//
// The windows differ only in what they put ABOVE this: the quick/full windows use user settings (history,
// result-type order), and the inline window layers directory proximity on top (see DirectoryProximity).
public readonly record struct MatchRank(int Tier, int Start, double Weight)
{
    // How the match was found, best first. A candidate's own name is the strongest evidence that the
    // query really describes it; an initials/shorthand alias means the user abbreviated; a full
    // transliteration means the whole reading had to be spelled out.
    public const int TierName = 0;
    public const int TierInitials = 1;
    public const int TierFull = 2;

    // Start == int.MaxValue marks "this text did not match the whole query" -- distinct from a match at
    // index 0, which is the best possible Start.
    public static MatchRank NoMatch => new(int.MaxValue, int.MaxValue, 0);

    public bool IsMatch => Start != int.MaxValue;
}
