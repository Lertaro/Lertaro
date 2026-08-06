namespace Lertaro.Core.SearchIndex.Fzf;

// ASCII lookup tables for the per-character calls in the match/score hot loops (GetClass,
// ToLowerInvariant, BonusFor) -- what reference fzf does with its charClassOfAscii table. Built BY
// CALLING the general implementations in FzfAlgorithm at init, so equivalence with the general path
// is by construction, not by reimplementation. Non-ASCII chars fall back to the general calls.
internal static class FzfCharTables
{
    public static readonly byte[] ClassOfAscii = new byte[128];
    public static readonly char[] LowerOfAscii = new char[128];
    public static readonly char[] UpperOfAscii = new char[128];
    // [scheme][previous << 3 | current] -- CharClass has 7 values, padded to 8 for shift indexing.
    private static readonly short[][] BonusTable = new short[3][];

    static FzfCharTables()
    {
        for (var c = 0; c < 128; c++)
        {
            ClassOfAscii[c] = (byte)FzfAlgorithm.GetClass((char)c);
            LowerOfAscii[c] = char.ToLowerInvariant((char)c);
            UpperOfAscii[c] = char.ToUpperInvariant((char)c);
        }
        for (var scheme = 0; scheme < 3; scheme++)
        {
            var table = new short[64];
            for (var previous = 0; previous < 7; previous++)
                for (var current = 0; current < 7; current++)
                    table[(previous << 3) | current] = (short)FzfAlgorithm.BonusFor((FzfAlgorithm.CharClass)previous, (FzfAlgorithm.CharClass)current, (FzfScoringScheme)scheme);
            BonusTable[scheme] = table;
        }
    }

    public static byte GetClass(char c) => c < 128 ? ClassOfAscii[c] : (byte)FzfAlgorithm.GetClass(c);

    public static byte GetClass(byte b) => ClassOfAscii[b];

    public static char ToLower(char c) => c < 128 ? LowerOfAscii[c] : char.ToLowerInvariant(c);

    public static byte ToLower(byte b) => (byte)LowerOfAscii[b];

    public static short Bonus(FzfScoringScheme scheme, byte previousClass, byte currentClass)
        => BonusTable[(int)scheme][(previousClass << 3) | currentClass];

    public static bool CharsEqual(char text, char pattern, bool caseSensitive)
        => caseSensitive ? text == pattern : ToLower(text) == pattern;

    public static bool CharsEqual(byte text, byte pattern, bool caseSensitive)
        => caseSensitive ? text == pattern : ToLower(text) == pattern;
}
