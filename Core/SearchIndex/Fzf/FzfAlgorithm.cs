namespace Lertaro.Core.SearchIndex.Fzf;

internal enum FzfTermKind
{
    Fuzzy,
    Exact,
    ExactBoundary,
    Prefix,
    Suffix,
    Equal
}

internal enum FzfScoringScheme
{
    Default,
    Path,
    History
}

internal readonly record struct FzfMatchResult(int Start, int End, int Score)
{
    public bool IsMatch => Start >= 0;
    public static FzfMatchResult NoMatch => new(-1, -1, 0);
}

internal static class FzfAlgorithm
{
    public const int ScoreMatch = 16;
    public const int ScoreGapStart = -3;
    public const int ScoreGapExtension = -1;
    public const int BonusBoundary = ScoreMatch / 2;
    public const int BonusNonWord = ScoreMatch / 2;
    public const int BonusCamel123 = BonusBoundary + ScoreGapExtension;
    public const int BonusConsecutive = -(ScoreGapStart + ScoreGapExtension);
    public const int BonusFirstCharMultiplier = 2;
    public const int BonusBoundaryWhite = BonusBoundary + 2;
    public const int BonusBoundaryDelimiter = BonusBoundary + 1;
    public const int MaxV2Cells = 250_000;

    public static FzfMatchResult Match(
        FzfTermKind kind,
        ReadOnlySpan<char> text,
        string pattern,
        bool caseSensitive,
        FzfScoringScheme scheme,
        FzfSlab? slab = null) => kind switch
        {
            FzfTermKind.Fuzzy => FzfFuzzyMatcher.FuzzyMatchV2(text, pattern, caseSensitive, scheme, slab),
            FzfTermKind.Exact => FzfExactMatcher.ExactMatch(text, pattern, caseSensitive, scheme, boundaryCheck: false),
            FzfTermKind.ExactBoundary => FzfExactMatcher.ExactMatch(text, pattern, caseSensitive, scheme, boundaryCheck: true),
            FzfTermKind.Prefix => FzfExactMatcher.PrefixMatch(text, pattern, caseSensitive, scheme),
            FzfTermKind.Suffix => FzfExactMatcher.SuffixMatch(text, pattern, caseSensitive, scheme),
            FzfTermKind.Equal => FzfExactMatcher.EqualMatch(text, pattern, caseSensitive, scheme),
            _ => FzfMatchResult.NoMatch
        };

    public static int BonusFor(CharClass previous, CharClass current, FzfScoringScheme scheme)
    {
        if (current >= CharClass.NonWord)
        {
            if (previous == CharClass.White)
                return BoundaryWhiteBonus(scheme);
            if (previous == CharClass.Delimiter)
                return BoundaryDelimiterBonus(scheme);
            if (previous == CharClass.NonWord)
                return BonusBoundary;
        }

        if ((previous == CharClass.Lower && current == CharClass.Upper) ||
            (previous != CharClass.Number && current == CharClass.Number))
            return BonusCamel123;

        return current switch
        {
            CharClass.NonWord or CharClass.Delimiter => BonusNonWord,
            CharClass.White => BoundaryWhiteBonus(scheme),
            _ => 0
        };
    }

    public static CharClass InitialClass(FzfScoringScheme scheme) => scheme == FzfScoringScheme.Path ? CharClass.Delimiter : CharClass.White;

    private static int BoundaryWhiteBonus(FzfScoringScheme scheme) => scheme == FzfScoringScheme.Default ? BonusBoundaryWhite : BonusBoundary;

    private static int BoundaryDelimiterBonus(FzfScoringScheme scheme) => scheme == FzfScoringScheme.History ? BonusBoundary : BonusBoundaryDelimiter;

    public static CharClass GetClass(char c)
    {
        if (c >= 'a' && c <= 'z')
            return CharClass.Lower;
        if (c >= 'A' && c <= 'Z')
            return CharClass.Upper;
        if (c >= '0' && c <= '9')
            return CharClass.Number;
        if (char.IsWhiteSpace(c))
            return CharClass.White;
        if (c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar || c == ':' || c == ';' || c == ',' || c == '|')
            return CharClass.Delimiter;
        if (char.IsLetter(c))
            return CharClass.Letter;
        return CharClass.NonWord;
    }

    public static ulong GetCharMask(ReadOnlySpan<char> span)
    {
        ulong mask = 0;
        for (var i = 0; i < span.Length; i++)
        {
            mask |= 1UL << MaskBit(char.ToLowerInvariant(span[i]));
        }
        return mask;
    }

    public static ulong GetCharMask(string text) => GetCharMask(text.AsSpan());

    // UTF-8 twin of GetCharMask for byte-emitted aliases (see AliasGenerationUtf8): alias segments
    // are overwhelmingly pure ASCII (pinyin syllables), where byte == char and lowering is the plain
    // A-Z shift -- the rare non-ASCII segment decodes and defers to the char version, so both
    // overloads provably bucket every input identically.
    public static ulong GetCharMaskUtf8(ReadOnlySpan<byte> utf8)
    {
        if (System.Text.Ascii.IsValid(utf8))
        {
            ulong mask = 0;
            for (var i = 0; i < utf8.Length; i++)
            {
                var c = (char)utf8[i];
                if (c >= 'A' && c <= 'Z')
                    c = (char)(c + 32);
                mask |= 1UL << MaskBit(c);
            }
            return mask;
        }

        var charCount = System.Text.Encoding.UTF8.GetCharCount(utf8);
        var tmp = charCount <= 512 ? stackalloc char[512] : new char[charCount];
        var written = System.Text.Encoding.UTF8.GetChars(utf8, tmp);
        return GetCharMask(tmp[..written]);
    }

    private static int MaskBit(char lower) => lower switch
    {
        >= 'a' and <= 'z' => lower - 'a',
        >= '0' and <= '9' => 26 + (lower - '0'),
        _ => 36 + (lower % 28)
    };

    public static int LeadingWhitespaces(ReadOnlySpan<char> text)
    {
        var i = 0;
        while (i < text.Length && char.IsWhiteSpace(text[i]))
            i++;
        return i;
    }

    public static int TrailingWhitespaces(ReadOnlySpan<char> text)
    {
        var count = 0;
        for (var i = text.Length - 1; i >= 0 && char.IsWhiteSpace(text[i]); i--)
            count++;
        return count;
    }

    public enum CharClass
    {
        White = 0,
        NonWord = 1,
        Delimiter = 2,
        Lower = 3,
        Upper = 4,
        Letter = 5,
        Number = 6
    }
}
