namespace Lertaro.Core.SearchIndex.Fzf;

// Position-tracking twin of FzfFuzzyMatcher's FuzzyMatchV2/V1 -- split into its own file purely to
// keep FzfFuzzyMatcher.cs under the repo's per-file line limit. Never called from the hot per-candidate
// scan (SearchMatcher/PathGate use FzfFuzzyMatcher.FuzzyMatchV2 directly) -- only HighlightMask's
// bounded weight refinement and on-screen highlighting call this, so this extra backtrace cost never
// touches the scan itself.
internal static class FzfPositionMatcher
{
    // Ranking/highlight-only twin of FuzzyMatchV2: same DP, but additionally recovers every matched
    // character's position into `marks` (index-aligned to `text`) via a fuller backtrace, instead of
    // just the match start. Replaces the old approach of re-deriving an approximate mask via a separate
    // DP (FuzzyHighlightMatcher) after the fact -- this recovers the REAL alignment the actual match
    // algorithm found.
    public static FzfMatchResult FuzzyMatchV2WithPositions(ReadOnlySpan<char> text, string pattern, bool caseSensitive, FzfScoringScheme scheme, Span<bool> marks, FzfSlab? slab = null)
    {
        var m = pattern.Length;
        if (m == 0)
            return new FzfMatchResult(0, 0, 0);
        var n = text.Length;
        if (m > n)
            return FzfMatchResult.NoMatch;
        if (!FzfScoring.FindFuzzyScope(text, pattern, caseSensitive, out var minIdx, out var maxIdx))
            return FzfMatchResult.NoMatch;

        var scopedLength = maxIdx - minIdx;
        if (m > 1000 || (long)scopedLength * m > FzfAlgorithm.MaxV2Cells)
            return FuzzyMatchV1WithPositions(text, pattern, caseSensitive, scheme, marks);

        var chars = slab?.Chars(scopedLength) ?? new char[scopedLength];
        var bonus = slab?.Bonus(scopedLength) ?? new short[scopedLength];
        var first = slab?.First(m) ?? new int[m];
        Array.Fill(first, -1, 0, m);

        var patternIndex = 0;
        var lastIdx = 0;
        var firstPatternChar = pattern[0];
        var previousClass = (byte)FzfAlgorithm.InitialClass(scheme);
        for (var offset = 0; offset < scopedLength; offset++)
        {
            var raw = text[minIdx + offset];
            var currentClass = FzfCharTables.GetClass(raw);
            var normalized = caseSensitive ? raw : FzfCharTables.ToLower(raw);
            chars[offset] = normalized;
            bonus[offset] = FzfCharTables.Bonus(scheme, previousClass, currentClass);
            previousClass = currentClass;

            if (patternIndex < m && normalized == pattern[patternIndex])
            {
                first[patternIndex] = offset;
                lastIdx = offset;
                patternIndex++;
            }
        }

        if (patternIndex != m)
            return FzfMatchResult.NoMatch;

        if (m == 1)
        {
            var bestScore = 0;
            var bestPos = -1;
            for (var i = 0; i < scopedLength; i++)
            {
                if (chars[i] != firstPatternChar)
                    continue;
                var score = FzfAlgorithm.ScoreMatch + bonus[i] * FzfAlgorithm.BonusFirstCharMultiplier;
                if (score > bestScore)
                {
                    bestScore = score;
                    bestPos = i;
                    if (bonus[i] >= FzfAlgorithm.BonusBoundary)
                        break;
                }
            }

            if (bestPos < 0)
                return FzfMatchResult.NoMatch;

            if (minIdx + bestPos < marks.Length)
                marks[minIdx + bestPos] = true;
            return new FzfMatchResult(minIdx + bestPos, minIdx + bestPos + 1, bestScore);
        }

        var f0 = first[0];
        var width = lastIdx - f0 + 1;
        var matrixLength = m * width;
        var scores = slab?.Scores(matrixLength) ?? new short[matrixLength];
        var consecutive = slab?.Consecutive(matrixLength) ?? new short[matrixLength];

        var inGap = false;
        short previous = 0;
        for (var col = f0; col <= lastIdx; col++)
        {
            var rel = col - f0;
            if (chars[col] == firstPatternChar)
            {
                var score = (short)(FzfAlgorithm.ScoreMatch + bonus[col] * FzfAlgorithm.BonusFirstCharMultiplier);
                scores[rel] = score;
                consecutive[rel] = 1;
                previous = score;
                inGap = false;
            }
            else
            {
                var score = (short)Math.Max(previous + (inGap ? FzfAlgorithm.ScoreGapExtension : FzfAlgorithm.ScoreGapStart), 0);
                scores[rel] = score;
                consecutive[rel] = 0;
                previous = score;
                inGap = true;
            }
        }

        var maxScore = 0;
        var maxScorePos = f0;
        for (var pidx = 1; pidx < m; pidx++)
        {
            var row = pidx * width;
            var previousRow = row - width;
            inGap = false;
            var start = first[pidx];
            var startRel = start - f0;
            if (startRel > 0)
            {
                scores[row + startRel - 1] = 0;
                consecutive[row + startRel - 1] = 0;
            }
            for (var col = start; col <= lastIdx; col++)
            {
                var rel = col - f0;
                var s2 = rel > 0
                    ? (short)(scores[row + rel - 1] + (inGap ? FzfAlgorithm.ScoreGapExtension : FzfAlgorithm.ScoreGapStart))
                    : (short)0;

                short s1 = 0;
                short consecutiveScore = 0;
                if (chars[col] == pattern[pidx] && rel > 0)
                {
                    s1 = (short)(scores[previousRow + rel - 1] + FzfAlgorithm.ScoreMatch);
                    var b = bonus[col];
                    consecutiveScore = (short)(consecutive[previousRow + rel - 1] + 1);
                    if (consecutiveScore > 1)
                    {
                        var firstBonus = bonus[col - consecutiveScore + 1];
                        if (b >= FzfAlgorithm.BonusBoundary && b > firstBonus)
                        {
                            consecutiveScore = 1;
                        }
                        else
                        {
                            b = (short)Math.Max(Math.Max((int)b, firstBonus), FzfAlgorithm.BonusConsecutive);
                        }
                    }

                    if (s1 + b < s2)
                    {
                        s1 += bonus[col];
                        consecutiveScore = 0;
                    }
                    else
                    {
                        s1 += b;
                    }
                }

                consecutive[row + rel] = consecutiveScore;
                inGap = s1 < s2;
                var cellScore = (short)Math.Max(Math.Max((int)s1, s2), 0);
                scores[row + rel] = cellScore;

                if (pidx == m - 1 && cellScore > maxScore)
                {
                    maxScore = cellScore;
                    maxScorePos = col;
                }
            }
        }

        var startIndex = FzfBacktrack.BacktrackPositions(scores, consecutive, first, f0, width, m, maxScorePos, minIdx, marks);
        return new FzfMatchResult(minIdx + startIndex, minIdx + maxScorePos + 1, maxScore);
    }

    // Rare-path twin of FuzzyMatchV1 (huge pattern, >1000 chars or enormous scope -- never reached by
    // real launcher queries) that also fills `marks` via a simple forward-greedy earliest-occurrence
    // walk from the shrunk window. Good enough for an edge case this size never occurs in practice.
    public static FzfMatchResult FuzzyMatchV1WithPositions(ReadOnlySpan<char> text, string pattern, bool caseSensitive, FzfScoringScheme scheme, Span<bool> marks)
    {
        if (pattern.Length == 0)
            return new FzfMatchResult(0, 0, 0);
        if (!FzfScoring.FindFuzzyScope(text, pattern, caseSensitive, out var start, out var end))
            return FzfMatchResult.NoMatch;

        var patternIndex = pattern.Length - 1;
        var shrinkStart = start;
        for (var i = end - 1; i >= start; i--)
        {
            if (FzfCharTables.CharsEqual(text[i], pattern[patternIndex], caseSensitive))
            {
                patternIndex--;
                if (patternIndex < 0)
                {
                    shrinkStart = i;
                    break;
                }
            }
        }

        var pIdx = 0;
        for (var i = shrinkStart; i < end && pIdx < pattern.Length; i++)
        {
            if (FzfCharTables.CharsEqual(text[i], pattern[pIdx], caseSensitive))
            {
                if (i < marks.Length)
                    marks[i] = true;
                pIdx++;
            }
        }

        var score = FzfScoring.CalculateScore(text, pattern, shrinkStart, end, caseSensitive, scheme);
        return new FzfMatchResult(shrinkStart, end, score);
    }
}
