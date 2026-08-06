namespace Lertaro.Core.SearchIndex.Fzf;

// DP traceback stage shared by FuzzyMatchV2 (start only) and FuzzyMatchV2WithPositions (full position
// recovery) in FzfFuzzyMatcher -- extracted into its own class (composition, not a partial class) purely
// to keep FzfFuzzyMatcher.cs under the repo's per-file line limit. Operates on the slab arrays only,
// never on the text, so it stays text-representation-agnostic and is also reused by FzfByteMatcher.
internal static class FzfBacktrack
{
    internal static int BacktrackStart(short[] scores, short[] consecutive, int[] first, int f0, int width, int patternLength, int maxScorePos)
    {
        var i = patternLength - 1;
        var j = maxScorePos;
        var preferMatch = true;
        while (i >= 0 && j >= first[i])
        {
            var row = i * width;
            var rel = j - f0;
            var score = scores[row + rel];
            var diagonal = i > 0 && rel > 0 ? scores[row - width + rel - 1] : (short)0;
            var left = rel > 0 ? scores[row + rel - 1] : (short)0;

            if (score > diagonal && (score > left || score == left && preferMatch))
            {
                if (i == 0)
                    return j;
                i--;
            }

            // Only consult the next row's cell if THIS match actually wrote it (each row is only
            // written from its own first[]-guard onward; the slab is reused, not zeroed). The old
            // guard was a raw array-length bound, so this read stale cells from whatever match used
            // the slab before -- making the chosen match START (a tie-break, never the score) depend
            // on which candidate happened to be matched previously on the same worker. An unwritten
            // cell semantically means "no match possible here," i.e. consecutive == 0.
            preferMatch = consecutive[row + rel] > 1 ||
                          (i < patternLength - 1 && rel + 1 < width && rel + 1 >= first[i + 1] - f0 - 1
                           && consecutive[row + width + rel + 1] > 0);
            j--;
        }

        return Math.Max(0, first[0]);
    }

    // Position-recovering twin of BacktrackStart: same walk, but marks every (i, j) cell it recognizes
    // as an actual character match (not just the final one at i == 0), then returns the match start
    // exactly as BacktrackStart does. If the walk exits before reaching i == 0 (the same defensive edge
    // case BacktrackStart itself guards with its own fallback return), whatever pattern indices weren't
    // reached get marked at their earliest possible occurrence (first[k]) so the result is still a
    // complete, valid position set rather than a partial one.
    internal static int BacktrackPositions(short[] scores, short[] consecutive, int[] first, int f0, int width, int patternLength, int maxScorePos, int minIdx, Span<bool> marks)
    {
        var i = patternLength - 1;
        var j = maxScorePos;
        var preferMatch = true;
        while (i >= 0 && j >= first[i])
        {
            var row = i * width;
            var rel = j - f0;
            var score = scores[row + rel];
            var diagonal = i > 0 && rel > 0 ? scores[row - width + rel - 1] : (short)0;
            var left = rel > 0 ? scores[row + rel - 1] : (short)0;

            if (score > diagonal && (score > left || score == left && preferMatch))
            {
                var pos = minIdx + j;
                if (pos < marks.Length)
                    marks[pos] = true;
                if (i == 0)
                    return j;
                i--;
            }

            preferMatch = consecutive[row + rel] > 1 ||
                          (i < patternLength - 1 && rel + 1 < width && rel + 1 >= first[i + 1] - f0 - 1
                           && consecutive[row + width + rel + 1] > 0);
            j--;
        }

        for (var k = 0; k <= i; k++)
        {
            var pos = minIdx + first[k];
            if (pos < marks.Length)
                marks[pos] = true;
        }

        return Math.Max(0, first[0]);
    }
}
