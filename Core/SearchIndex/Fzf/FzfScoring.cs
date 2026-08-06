namespace Lertaro.Core.SearchIndex.Fzf;

internal static class FzfScoring
{
    public static bool FindFuzzyScope(ReadOnlySpan<char> text, string pattern, bool caseSensitive, out int start, out int end)
    {
        start = -1;
        end = -1;

        var currentIdx = 0;
        var lastChar = '\0';

        for (var patternIndex = 0; patternIndex < pattern.Length; patternIndex++)
        {
            var target = pattern[patternIndex];
            int offset;
            if (caseSensitive)
            {
                offset = text.Slice(currentIdx).IndexOf(target);
            }
            else
            {
                var lower = char.ToLowerInvariant(target);
                var upper = char.ToUpperInvariant(target);
                offset = lower == upper
                    ? text.Slice(currentIdx).IndexOf(lower)
                    : text.Slice(currentIdx).IndexOfAny(lower, upper);
            }

            if (offset < 0)
                return false;

            var absoluteIdx = currentIdx + offset;
            if (patternIndex == 0)
                start = Math.Max(0, absoluteIdx - 1);

            lastChar = target;
            currentIdx = absoluteIdx + 1;
        }

        end = currentIdx;

        var l = char.ToLowerInvariant(lastChar);
        var u = char.ToUpperInvariant(lastChar);
        var lastOffset = caseSensitive ? text.Slice(end).LastIndexOf(lastChar)
            : (l == u ? text.Slice(end).LastIndexOf(l) : text.Slice(end).LastIndexOfAny(l, u));

        if (lastOffset >= 0)
            end = end + lastOffset + 1;

        return true;
    }

    public static int CalculateScore(ReadOnlySpan<char> text, string pattern, int start, int end, bool caseSensitive, FzfScoringScheme scheme)
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
}
