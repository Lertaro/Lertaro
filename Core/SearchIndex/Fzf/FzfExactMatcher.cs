namespace Lertaro.Core.SearchIndex.Fzf;

internal static class FzfExactMatcher
{
    public static FzfMatchResult ExactMatch(ReadOnlySpan<char> text, string pattern, bool caseSensitive, FzfScoringScheme scheme, bool boundaryCheck)
    {
        if (pattern.Length == 0 || pattern.Length > text.Length)
            return FzfMatchResult.NoMatch;

        var bestPos = -1;
        var bestBonus = -1;

        var patternSpan = pattern.AsSpan();
        var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        var offset = 0;
        while (offset <= text.Length - pattern.Length)
        {
            var index = text.Slice(offset).IndexOf(patternSpan, comparison);
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
            : FzfScoring.CalculateScore(text, pattern, bestPos, end, caseSensitive, scheme);
        return new FzfMatchResult(bestPos, end, score);
    }

    public static FzfMatchResult PrefixMatch(ReadOnlySpan<char> text, string pattern, bool caseSensitive, FzfScoringScheme scheme)
    {
        if (pattern.Length == 0)
            return new FzfMatchResult(0, 0, 0);
        var start = char.IsWhiteSpace(pattern[0]) ? 0 : FzfAlgorithm.LeadingWhitespaces(text);
        if (text.Length - start < pattern.Length || !SpanEquals(text, start, pattern, caseSensitive))
            return FzfMatchResult.NoMatch;

        var end = start + pattern.Length;
        return new FzfMatchResult(start, end, FzfScoring.CalculateScore(text, pattern, start, end, caseSensitive, scheme));
    }

    public static FzfMatchResult SuffixMatch(ReadOnlySpan<char> text, string pattern, bool caseSensitive, FzfScoringScheme scheme)
    {
        var trimmedLength = pattern.Length == 0 || !char.IsWhiteSpace(pattern[^1])
            ? text.Length - FzfAlgorithm.TrailingWhitespaces(text)
            : text.Length;
        if (pattern.Length == 0)
            return new FzfMatchResult(trimmedLength, trimmedLength, 0);

        var start = trimmedLength - pattern.Length;
        if (start < 0 || !SpanEquals(text, start, pattern, caseSensitive))
            return FzfMatchResult.NoMatch;

        return new FzfMatchResult(start, trimmedLength, FzfScoring.CalculateScore(text, pattern, start, trimmedLength, caseSensitive, scheme));
    }

    public static FzfMatchResult EqualMatch(ReadOnlySpan<char> text, string pattern, bool caseSensitive, FzfScoringScheme scheme)
    {
        if (pattern.Length == 0)
            return FzfMatchResult.NoMatch;
        var start = char.IsWhiteSpace(pattern[0]) ? 0 : FzfAlgorithm.LeadingWhitespaces(text);
        var trailing = char.IsWhiteSpace(pattern[^1]) ? 0 : FzfAlgorithm.TrailingWhitespaces(text);
        if (text.Length - start - trailing != pattern.Length || !SpanEquals(text, start, pattern, caseSensitive))
            return FzfMatchResult.NoMatch;

        return new FzfMatchResult(
            start,
            start + pattern.Length,
            (FzfAlgorithm.ScoreMatch + FzfAlgorithm.BonusBoundaryWhite) * pattern.Length + (FzfAlgorithm.BonusFirstCharMultiplier - 1) * FzfAlgorithm.BonusBoundaryWhite);
    }

    public static int BonusAt(ReadOnlySpan<char> text, int index, FzfScoringScheme scheme)
    {
        if (index == 0)
            return FzfCharTables.Bonus(scheme, (byte)FzfAlgorithm.InitialClass(scheme), FzfCharTables.GetClass(text[index]));
        return FzfCharTables.Bonus(scheme, FzfCharTables.GetClass(text[index - 1]), FzfCharTables.GetClass(text[index]));
    }

    private static bool SpanEquals(ReadOnlySpan<char> text, int start, string pattern, bool caseSensitive) => text.Slice(start, pattern.Length).Equals(pattern, caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);

    private static bool IsBoundaryMatch(ReadOnlySpan<char> text, int start, int end, int startBonus)
    {
        if (startBonus < FzfAlgorithm.BonusBoundary)
            return false;
        if (start > 0 && FzfCharTables.GetClass(text[start - 1]) > (byte)FzfAlgorithm.CharClass.Delimiter)
            return false;
        return end >= text.Length || FzfCharTables.GetClass(text[end]) <= (byte)FzfAlgorithm.CharClass.Delimiter;
    }
}
