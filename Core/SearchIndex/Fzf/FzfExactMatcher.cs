namespace Lertaro.Core.SearchIndex.Fzf;

internal static class FzfExactMatcher
{
    public static FzfMatchResult ExactMatch(ReadOnlySpan<char> text, string pattern, bool caseSensitive, FzfScoringScheme scheme)
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
            if (bonus > bestBonus)
            {
                bestPos = i;
                bestBonus = bonus;
                if (bonus >= FzfAlgorithm.BonusBoundary)
                    break;
            }

            offset = i + 1;
        }

        if (bestPos < 0)
            return FzfMatchResult.NoMatch;

        var end = bestPos + pattern.Length;
        return new FzfMatchResult(bestPos, end, FzfScoring.CalculateScore(text, pattern, bestPos, end, caseSensitive, scheme));
    }

    public static int BonusAt(ReadOnlySpan<char> text, int index, FzfScoringScheme scheme)
    {
        if (index == 0)
            return FzfCharTables.Bonus(scheme, (byte)FzfAlgorithm.InitialClass(scheme), FzfCharTables.GetClass(text[index]));
        return FzfCharTables.Bonus(scheme, FzfCharTables.GetClass(text[index - 1]), FzfCharTables.GetClass(text[index]));
    }
}
