using System.Text;

namespace Lertaro.Core.SearchIndex.Fzf;

// Byte-side view of a parsed FzfPattern: each term's text pre-encoded to ASCII bytes once per query.
// A term whose text is NOT pure ASCII gets null bytes -- it can never match ASCII text (a >127 char
// simply cannot occur), which TryMatch below treats as an immediate per-term NoMatch (still honoring
// inverse semantics). Prepared once per query, so per-candidate work stays allocation-free.
internal sealed class FzfBytePattern
{
    internal readonly record struct ByteTerm(FzfTermKind Kind, bool Inverse, byte[]? Bytes, bool CaseSensitive);
    internal readonly record struct ByteTermSet(ByteTerm[] Terms);

    public readonly ByteTermSet[] TermSets;

    private FzfBytePattern(ByteTermSet[] termSets) => TermSets = termSets;

    public static FzfBytePattern From(FzfPattern pattern)
    {
        var sets = new ByteTermSet[pattern.TermSets.Length];
        for (var s = 0; s < pattern.TermSets.Length; s++)
        {
            var terms = pattern.TermSets[s].Terms;
            var byteTerms = new ByteTerm[terms.Length];
            for (var t = 0; t < terms.Length; t++)
            {
                var text = terms[t].Text;
                var bytes = Ascii.IsValid(text) ? Encoding.ASCII.GetBytes(text) : null;
                byteTerms[t] = new ByteTerm(terms[t].Kind, terms[t].Inverse, bytes, terms[t].CaseSensitive);
            }
            sets[s] = new ByteTermSet(byteTerms);
        }
        return new FzfBytePattern(sets);
    }

    public static FzfMatchResult Match(FzfTermKind kind, ReadOnlySpan<byte> text, byte[] pattern, bool caseSensitive, FzfScoringScheme scheme, FzfSlab slab, FzfByteBuffers buffers) => kind switch
    {
        FzfTermKind.Fuzzy => FzfByteMatcher.FuzzyMatchV2(text, pattern, caseSensitive, scheme, slab, buffers),
        FzfTermKind.Exact => FzfByteExactMatcher.ExactMatch(text, pattern, caseSensitive, scheme, boundaryCheck: false),
        FzfTermKind.ExactBoundary => FzfByteExactMatcher.ExactMatch(text, pattern, caseSensitive, scheme, boundaryCheck: true),
        FzfTermKind.Prefix => FzfByteExactMatcher.PrefixMatch(text, pattern, caseSensitive, scheme),
        FzfTermKind.Suffix => FzfByteExactMatcher.SuffixMatch(text, pattern, caseSensitive, scheme),
        FzfTermKind.Equal => FzfByteExactMatcher.EqualMatch(text, pattern, caseSensitive, scheme),
        _ => FzfMatchResult.NoMatch
    };

    public bool TryMatch(ReadOnlySpan<byte> text, out FzfPatternResult result, FzfScoringScheme scheme, FzfSlab slab, FzfByteBuffers buffers)
    {
        var totalScore = 0;
        var minBegin = int.MaxValue;
        var minEnd = int.MaxValue;
        var maxEnd = 0;
        var validOffsetFound = false;

        foreach (var set in TermSets)
        {
            var matched = false;
            FzfMatchResult best = default;
            foreach (var term in set.Terms)
            {
                var current = term.Bytes == null
                    ? FzfMatchResult.NoMatch // non-ASCII pattern text can never occur in ASCII text
                    : Match(term.Kind, text, term.Bytes, term.CaseSensitive, scheme, slab, buffers);
                if (current.IsMatch)
                {
                    if (term.Inverse)
                    {
                        matched = false;
                        best = default;
                        break;
                    }

                    matched = true;
                    best = current;
                    break;
                }

                if (term.Inverse)
                {
                    matched = true;
                    best = new FzfMatchResult(0, 0, 0);
                }
            }

            if (!matched)
            {
                result = default;
                return false;
            }

            totalScore += best.Score;
            if (best.Start < best.End)
            {
                minBegin = Math.Min(minBegin, best.Start);
                minEnd = Math.Min(minEnd, best.End);
                maxEnd = Math.Max(maxEnd, best.End);
                validOffsetFound = true;
            }
        }

        result = new FzfPatternResult(totalScore, minBegin, minEnd, maxEnd, validOffsetFound);
        return true;
    }

    // '|' polyphonic-alias segmentation on bytes -- mirrors FzfPattern.TryMatch's segmented branch.
    public bool TryMatchSegmented(ReadOnlySpan<byte> text, out FzfPatternResult result, FzfScoringScheme scheme, FzfSlab slab, FzfByteBuffers buffers)
    {
        if (!text.Contains((byte)'|'))
            return TryMatch(text, out result, scheme, slab, buffers);

        var bestResult = default(FzfPatternResult);
        var matchedAny = false;
        var start = 0;
        while (start < text.Length)
        {
            var len = text.Slice(start).IndexOf((byte)'|');
            if (len < 0)
                len = text.Length - start;

            if (TryMatch(text.Slice(start, len), out var segmentResult, scheme, slab, buffers))
            {
                if (segmentResult.ValidOffsetFound)
                {
                    segmentResult = new FzfPatternResult(
                        segmentResult.Score,
                        segmentResult.MinBegin + start,
                        segmentResult.MinEnd + start,
                        segmentResult.MaxEnd + start,
                        true);
                }

                if (!matchedAny || segmentResult.Score > bestResult.Score)
                {
                    bestResult = segmentResult;
                    matchedAny = true;
                }
            }

            start += len + 1;
        }

        result = bestResult;
        return matchedAny;
    }

    // Byte twin of FzfResultRank.RankLow32.
    public static uint RankLow32(ReadOnlySpan<byte> text, FzfPatternResult match)
        => MatchPositionPoint(text, match) | ((uint)(match.ValidOffsetFound ? ClampToUShort(Math.Max(0, match.MaxEnd - match.MinBegin)) : ushort.MaxValue) << 16);

    // Byte twin of FzfResultRank.ForDefaultScheme -- offsets are identical for ASCII text.
    public static FzfRank ForDefaultScheme(int entryIndex, ReadOnlySpan<byte> text, FzfPatternResult match)
    {
        var point0 = MatchPositionPoint(text, match);
        var point1 = match.ValidOffsetFound ? ClampToUShort(Math.Max(0, match.MaxEnd - match.MinBegin)) : ushort.MaxValue;
        var point2 = (ushort)(ushort.MaxValue - ClampToUShort(match.Score));
        var point3 = ClampToUShort(TrimmedLength(text));
        return new FzfRank(entryIndex, match.Score, point0 | ((ulong)point1 << 16) | ((ulong)point3 << 32) | ((ulong)point2 << 48));
    }

    // Ranking-only weight, computed as a bounded refinement AFTER the hot scan (see
    // FzfResultRank.ApplyWeight) rather than inline per-candidate -- see that comment for why. ASCII
    // bytes widen 1:1 into chars (no decode table needed for values < 128), so HighlightMask's
    // char-based mask computation applies unchanged. Shared by name mode's refinement stage and path
    // mode's per-unique filename weight (SearchMatcherPath.PathMatchOne / PathGate).
    public static double ComputeWeight(ReadOnlySpan<byte> text, FzfPattern pattern)
    {
        if (pattern.IsEmpty)
            return 1.0;

        var widened = text.Length <= 512 ? stackalloc char[text.Length] : new char[text.Length];
        for (var i = 0; i < text.Length; i++)
            widened[i] = (char)text[i];
        return HighlightMask.ComputeWeight(widened, pattern);
    }

    private static int TrimmedLength(ReadOnlySpan<byte> text)
        => text.Length - FzfByteExactMatcher.LeadingWhitespaces(text) - FzfByteExactMatcher.TrailingWhitespaces(text) is var len && len > 0 ? len : 0;

    private static ushort MatchPositionPoint(ReadOnlySpan<byte> text, FzfPatternResult match)
    {
        if (!match.ValidOffsetFound)
            return ushort.MaxValue;
        if (match.MinBegin <= 0)
            return 0;
        return ClampToUShort((IsWordBoundary(text, match.MinBegin) ? 256 : 4096) + match.MinBegin);
    }

    private static bool IsWordBoundary(ReadOnlySpan<byte> text, int index)
    {
        if (index <= 0 || index >= text.Length)
            return index == 0;

        var previous = (char)text[index - 1];
        var current = (char)text[index];
        if (!char.IsLetterOrDigit(previous))
            return true;

        return char.IsLower(previous) && (char.IsUpper(current) || char.IsDigit(current));
    }

    private static ushort ClampToUShort(int value)
    {
        if (value <= 0)
            return 0;
        return value >= ushort.MaxValue ? ushort.MaxValue : (ushort)value;
    }
}
