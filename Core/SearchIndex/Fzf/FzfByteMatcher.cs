
namespace Lertaro.Core.SearchIndex.Fzf;

// Byte-path fuzzy matcher: for a pure-ASCII name matched against a pure-ASCII pattern, UTF-8 bytes ARE
// the chars (1:1, same offsets), so the whole match can run on the raw snapshot bytes with zero
// decode. Same algorithm as FzfFuzzyMatcher; only the text representation differs. The DP-side slab
// arrays (bonus/first/scores/consecutive) are shared with the char path; only the normalized-text
// buffer needs its own byte scratch.
internal sealed class FzfByteBuffers
{
    private byte[] _norm = new byte[256];

    public byte[] Norm(int length)
    {
        if (_norm.Length < length)
            _norm = new byte[Math.Max(length, _norm.Length * 2)];
        return _norm;
    }
}

internal static class FzfByteMatcher
{
    public static FzfMatchResult FuzzyMatchV2(ReadOnlySpan<byte> text, byte[] pattern, bool caseSensitive, FzfScoringScheme scheme, FzfSlab slab, FzfByteBuffers buffers)
    {
        var m = pattern.Length;
        if (m == 0)
            return new FzfMatchResult(0, 0, 0);
        var n = text.Length;
        if (m > n)
            return FzfMatchResult.NoMatch;
        if (!FindFuzzyScope(text, pattern, caseSensitive, out var minIdx, out var maxIdx))
            return FzfMatchResult.NoMatch;

        var scopedLength = maxIdx - minIdx;
        if (m > 1000 || (long)scopedLength * m > FzfAlgorithm.MaxV2Cells)
            return FuzzyMatchV1(text, pattern, caseSensitive, scheme);

        var chars = buffers.Norm(scopedLength);
        var bonus = slab.Bonus(scopedLength);
        var first = slab.First(m);
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

            return bestPos >= 0
                ? new FzfMatchResult(minIdx + bestPos, minIdx + bestPos + 1, bestScore)
                : FzfMatchResult.NoMatch;
        }

        var f0 = first[0];
        var width = lastIdx - f0 + 1;
        var matrixLength = m * width;
        var scores = slab.Scores(matrixLength);
        var consecutive = slab.Consecutive(matrixLength);

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

        var startIndex = FzfBacktrack.BacktrackStart(scores, consecutive, first, f0, width, m, maxScorePos);
        return new FzfMatchResult(minIdx + startIndex, minIdx + maxScorePos + 1, maxScore);
    }

    public static FzfMatchResult FuzzyMatchV1(ReadOnlySpan<byte> text, byte[] pattern, bool caseSensitive, FzfScoringScheme scheme)
    {
        if (pattern.Length == 0)
            return new FzfMatchResult(0, 0, 0);
        if (!FindFuzzyScope(text, pattern, caseSensitive, out var start, out var end))
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

        var score = FzfByteExactMatcher.CalculateScore(text, pattern, shrinkStart, end, caseSensitive, scheme);
        return new FzfMatchResult(shrinkStart, end, score);
    }

    public static bool FindFuzzyScope(ReadOnlySpan<byte> text, byte[] pattern, bool caseSensitive, out int start, out int end)
    {
        start = -1;
        end = -1;

        var currentIdx = 0;
        byte lastChar = 0;

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
                var lower = (byte)FzfCharTables.LowerOfAscii[target];
                var upper = (byte)FzfCharTables.UpperOfAscii[target];
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

        var l = (byte)FzfCharTables.LowerOfAscii[lastChar];
        var u = (byte)FzfCharTables.UpperOfAscii[lastChar];
        var lastOffset = caseSensitive ? text.Slice(end).LastIndexOf(lastChar)
            : (l == u ? text.Slice(end).LastIndexOf(l) : text.Slice(end).LastIndexOfAny(l, u));

        if (lastOffset >= 0)
            end = end + lastOffset + 1;

        return true;
    }
}
