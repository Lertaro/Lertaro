
namespace Lertaro.Core.SearchIndex.Fzf;

// Byte-path exact/prefix/suffix/equal matchers + CalculateScore -- see FzfByteMatcher. ASCII-only by
// contract (callers gate on Ascii.IsValid for both text and pattern), so byte comparisons with the
// ASCII case tables are exactly equivalent to the char path.
internal static class FzfByteExactMatcher
{
    public static FzfMatchResult ExactMatch(ReadOnlySpan<byte> text, byte[] pattern, bool caseSensitive, FzfScoringScheme scheme, bool boundaryCheck)
    {
        if (pattern.Length == 0 || pattern.Length > text.Length)
            return FzfMatchResult.NoMatch;

        var bestPos = -1;
        var bestBonus = -1;

        var offset = 0;
        while (offset <= text.Length - pattern.Length)
        {
            var index = IndexOfPattern(text.Slice(offset), pattern, caseSensitive);
            if (index < 0)
                break;

            var i = offset + index;
            var bonus = BonusAt(text, i, scheme);
            if (!boundaryCheck || IsBoundaryMatch(text, i, i + pattern.Length, bonus))
            {
                if (bonus > bestBonus)
                {
                    bestPos = i;
                    bestBonus = bonus;
                    if (bonus >= FzfAlgorithm.BonusBoundary)
                        break;
                }
            }

            offset = i + 1;
        }

        if (bestPos < 0)
            return FzfMatchResult.NoMatch;

        var end = bestPos + pattern.Length;
        var score = boundaryCheck
            ? FzfAlgorithm.ScoreMatch * pattern.Length + FzfAlgorithm.BonusBoundaryWhite * (pattern.Length + 1) + bestBonus
            : CalculateScore(text, pattern, bestPos, end, caseSensitive, scheme);
        return new FzfMatchResult(bestPos, end, score);
    }

    public static FzfMatchResult PrefixMatch(ReadOnlySpan<byte> text, byte[] pattern, bool caseSensitive, FzfScoringScheme scheme)
    {
        if (pattern.Length == 0)
            return new FzfMatchResult(0, 0, 0);
        var start = IsAsciiWhite(pattern[0]) ? 0 : LeadingWhitespaces(text);
        if (text.Length - start < pattern.Length || !RegionEquals(text, start, pattern, caseSensitive))
            return FzfMatchResult.NoMatch;

        var end = start + pattern.Length;
        return new FzfMatchResult(start, end, CalculateScore(text, pattern, start, end, caseSensitive, scheme));
    }

    public static FzfMatchResult SuffixMatch(ReadOnlySpan<byte> text, byte[] pattern, bool caseSensitive, FzfScoringScheme scheme)
    {
        var trimmedLength = pattern.Length == 0 || !IsAsciiWhite(pattern[^1])
            ? text.Length - TrailingWhitespaces(text)
            : text.Length;
        if (pattern.Length == 0)
            return new FzfMatchResult(trimmedLength, trimmedLength, 0);

        var start = trimmedLength - pattern.Length;
        if (start < 0 || !RegionEquals(text, start, pattern, caseSensitive))
            return FzfMatchResult.NoMatch;

        return new FzfMatchResult(start, trimmedLength, CalculateScore(text, pattern, start, trimmedLength, caseSensitive, scheme));
    }

    public static FzfMatchResult EqualMatch(ReadOnlySpan<byte> text, byte[] pattern, bool caseSensitive, FzfScoringScheme scheme)
    {
        if (pattern.Length == 0)
            return FzfMatchResult.NoMatch;
        var start = IsAsciiWhite(pattern[0]) ? 0 : LeadingWhitespaces(text);
        var trailing = IsAsciiWhite(pattern[^1]) ? 0 : TrailingWhitespaces(text);
        if (text.Length - start - trailing != pattern.Length || !RegionEquals(text, start, pattern, caseSensitive))
            return FzfMatchResult.NoMatch;

        return new FzfMatchResult(
            start,
            start + pattern.Length,
            (FzfAlgorithm.ScoreMatch + FzfAlgorithm.BonusBoundaryWhite) * pattern.Length + (FzfAlgorithm.BonusFirstCharMultiplier - 1) * FzfAlgorithm.BonusBoundaryWhite);
    }

    public static int CalculateScore(ReadOnlySpan<byte> text, byte[] pattern, int start, int end, bool caseSensitive, FzfScoringScheme scheme)
    {
        var patternIndex = 0;
        var score = 0;
        var inGap = false;
        var consecutive = 0;
        var firstBonus = 0;
        var previousClass = start > 0 ? FzfCharTables.GetClass(text[start - 1]) : (byte)FzfAlgorithm.InitialClass(scheme);

        for (var i = start; i < end; i++)
        {
            var currentClass = FzfCharTables.GetClass(text[i]);
            var matched = patternIndex < pattern.Length && FzfCharTables.CharsEqual(text[i], pattern[patternIndex], caseSensitive);
            if (matched)
            {
                int bonus = FzfCharTables.Bonus(scheme, previousClass, currentClass);
                score += FzfAlgorithm.ScoreMatch;
                if (consecutive == 0)
                {
                    firstBonus = bonus;
                }
                else
                {
                    if (bonus >= FzfAlgorithm.BonusBoundary && bonus > firstBonus)
                        firstBonus = bonus;
                    bonus = Math.Max(Math.Max(bonus, firstBonus), FzfAlgorithm.BonusConsecutive);
                }

                score += patternIndex == 0 ? bonus * FzfAlgorithm.BonusFirstCharMultiplier : bonus;
                patternIndex++;
                consecutive++;
                inGap = false;
            }
            else
            {
                score += inGap ? FzfAlgorithm.ScoreGapExtension : FzfAlgorithm.ScoreGapStart;
                inGap = true;
                consecutive = 0;
                firstBonus = 0;
            }

            previousClass = currentClass;
        }

        return patternIndex == pattern.Length ? score : -1;
    }

    public static int BonusAt(ReadOnlySpan<byte> text, int index, FzfScoringScheme scheme)
    {
        if (index == 0)
            return FzfCharTables.Bonus(scheme, (byte)FzfAlgorithm.InitialClass(scheme), FzfCharTables.GetClass(text[index]));
        return FzfCharTables.Bonus(scheme, FzfCharTables.GetClass(text[index - 1]), FzfCharTables.GetClass(text[index]));
    }

    public static int LeadingWhitespaces(ReadOnlySpan<byte> text)
    {
        var i = 0;
        while (i < text.Length && IsAsciiWhite(text[i]))
            i++;
        return i;
    }

    public static int TrailingWhitespaces(ReadOnlySpan<byte> text)
    {
        var count = 0;
        for (var i = text.Length - 1; i >= 0 && IsAsciiWhite(text[i]); i--)
            count++;
        return count;
    }

    // char.IsWhiteSpace equivalent for the ASCII range: space + \t \n \v \f \r.
    public static bool IsAsciiWhite(byte b) => b == ' ' || (b >= '\t' && b <= '\r');

    // Case-aware IndexOf: vectorized IndexOf/IndexOfAny on the first byte, then a bytewise
    // case-folded verify -- the byte-span counterpart of IndexOf(span, StringComparison).
    private static int IndexOfPattern(ReadOnlySpan<byte> text, byte[] pattern, bool caseSensitive)
    {
        var lower0 = (byte)FzfCharTables.LowerOfAscii[pattern[0]];
        var upper0 = (byte)FzfCharTables.UpperOfAscii[pattern[0]];
        var offset = 0;
        while (offset <= text.Length - pattern.Length)
        {
            var index = caseSensitive
                ? text.Slice(offset).IndexOf(pattern[0])
                : lower0 == upper0 ? text.Slice(offset).IndexOf(lower0) : text.Slice(offset).IndexOfAny(lower0, upper0);
            if (index < 0)
                return -1;

            var start = offset + index;
            if (start > text.Length - pattern.Length)
                return -1;
            if (RegionEquals(text, start, pattern, caseSensitive))
                return start;
            offset = start + 1;
        }
        return -1;
    }

    private static bool RegionEquals(ReadOnlySpan<byte> text, int start, byte[] pattern, bool caseSensitive)
    {
        if (caseSensitive)
            return text.Slice(start, pattern.Length).SequenceEqual(pattern);
        for (var i = 0; i < pattern.Length; i++)
        {
            if (FzfCharTables.ToLower(text[start + i]) != FzfCharTables.ToLower(pattern[i]))
                return false;
        }
        return true;
    }

    private static bool IsBoundaryMatch(ReadOnlySpan<byte> text, int start, int end, int startBonus)
    {
        if (startBonus < FzfAlgorithm.BonusBoundary)
            return false;
        if (start > 0 && FzfCharTables.GetClass(text[start - 1]) > (byte)FzfAlgorithm.CharClass.Delimiter)
            return false;
        return end >= text.Length || FzfCharTables.GetClass(text[end]) <= (byte)FzfAlgorithm.CharClass.Delimiter;
    }
}
