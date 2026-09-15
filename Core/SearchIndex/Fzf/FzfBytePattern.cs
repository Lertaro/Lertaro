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
    internal readonly record struct ByteTermGroup(ByteTermSet[] Sets);

    public readonly ByteTermSet[] TermSets;

    // AND-first counterpart of the char-side FzfPattern.OrGroups: non-null only for a query that mixes
    // '|' with spaces under AND-first precedence, in which case this is the authoritative shape.
    public readonly ByteTermGroup[]? OrGroups;

    // True when the query carried "regex:/.../" clauses, which this byte matcher CANNOT apply: it is the
    // ASCII fast path, and a regex needs the decoded chars. A caller that ignores this gets one of two
    // wrong answers -- a regex-only query looks like "no positive terms" and is rejected for every name
    // (so the search returns nothing at all), while a query that also has an ordinary term matches on the
    // term alone and silently drops the regex (so it returns files the user excluded). Either way the
    // caller has to send these candidates down the char path, which does apply the clauses.
    public readonly bool HasRegexClauses;

    // Carried over from FzfPattern: false for an exclusion-only query, which must match nothing. The byte
    // path is the one that RUNS for pure-ASCII names (and it returns a hit before the char path is ever
    // consulted), so it needs the same guard -- without it ":temp" would match every ASCII name that does
    // not contain "temp", i.e. nearly the whole index.
    private readonly bool _hasPositiveTerm;

    private FzfBytePattern(ByteTermSet[] termSets, ByteTermGroup[]? orGroups, bool hasPositiveTerm, bool hasRegexClauses)
    {
        TermSets = termSets;
        OrGroups = orGroups;
        _hasPositiveTerm = hasPositiveTerm;
        HasRegexClauses = hasRegexClauses;
    }

    public static FzfBytePattern From(FzfPattern pattern)
    {
        var sets = Convert(pattern.TermSets);
        var hasPositiveTerm = pattern.HasPositiveTerm;
        var hasRegexes = pattern.Regexes is { Length: > 0 };
        return pattern.OrGroups == null
            ? new FzfBytePattern(sets, null, hasPositiveTerm, hasRegexes)
            : new FzfBytePattern(sets, Convert(pattern.OrGroups), hasPositiveTerm, hasRegexes);
    }

    private static ByteTermSet[] Convert(FzfTermSet[] source)
    {
        var sets = new ByteTermSet[source.Length];
        for (var s = 0; s < source.Length; s++)
        {
            var terms = source[s].Terms;
            var byteTerms = new ByteTerm[terms.Length];
            for (var t = 0; t < terms.Length; t++)
            {
                var text = terms[t].Text;
                var bytes = Ascii.IsValid(text) ? Encoding.ASCII.GetBytes(text) : null;
                byteTerms[t] = new ByteTerm(terms[t].Kind, terms[t].Inverse, bytes, terms[t].CaseSensitive);
            }
            sets[s] = new ByteTermSet(byteTerms);
        }
        return sets;
    }

    private static ByteTermGroup[] Convert(FzfTermGroup[] source)
    {
        var groups = new ByteTermGroup[source.Length];
        for (var i = 0; i < source.Length; i++)
            groups[i] = new ByteTermGroup(Convert(source[i].Sets));
        return groups;
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
        // Exclusion-only query: matches nothing, for the reason given on _hasPositiveTerm.
        if (!_hasPositiveTerm)
        {
            result = default;
            return false;
        }

        // AND-first mix: groups are OR alternatives, so the first group satisfying all of its terms wins.
        if (OrGroups != null)
        {
            foreach (var group in OrGroups)
            {
                if (TryMatchGroup(group, text, out result, scheme, slab, buffers))
                    return true;
            }

            result = default;
            return false;
        }

        var totalScore = 0;
        var minBegin = int.MaxValue;
        var minEnd = int.MaxValue;
        var maxEnd = 0;
        var validOffsetFound = false;

        foreach (var set in TermSets)
        {
            if (!TryMatchSet(set, text, out var best, scheme, slab, buffers))
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

    // Byte twin of FzfPattern's DNF group evaluation: every term set must be satisfied, while each set
    // keeps its own OR alternatives (including alias spellings).
    private static bool TryMatchGroup(ByteTermGroup group, ReadOnlySpan<byte> text, out FzfPatternResult result, FzfScoringScheme scheme, FzfSlab slab, FzfByteBuffers buffers)
    {
        var totalScore = 0;
        var minBegin = int.MaxValue;
        var minEnd = int.MaxValue;
        var maxEnd = 0;
        var validOffsetFound = false;

        foreach (var set in group.Sets)
        {
            if (!TryMatchSet(set, text, out var current, scheme, slab, buffers))
            {
                result = default;
                return false;
            }

            totalScore += current.Score;
            if (current.Start < current.End)
            {
                minBegin = Math.Min(minBegin, current.Start);
                minEnd = Math.Min(minEnd, current.End);
                maxEnd = Math.Max(maxEnd, current.End);
                validOffsetFound = true;
            }
        }

        result = new FzfPatternResult(totalScore, minBegin, minEnd, maxEnd, validOffsetFound);
        return true;
    }

    // One AND-condition's OR alternatives (the flat shape) or one AND-group of the DNF shape -- the
    // two are the same evaluation, so both callers share it.
    private static bool TryMatchSet(ByteTermSet set, ReadOnlySpan<byte> text, out FzfMatchResult best, FzfScoringScheme scheme, FzfSlab slab, FzfByteBuffers buffers)
    {
        best = default;
        foreach (var term in set.Terms)
        {
            var current = term.Bytes == null
                ? FzfMatchResult.NoMatch // non-ASCII pattern text can never occur in ASCII text
                : Match(term.Kind, text, term.Bytes, term.CaseSensitive, scheme, slab, buffers);
            if (current.IsMatch)
            {
                if (term.Inverse)
                    return false;

                best = current;
                return true;
            }

            if (term.Inverse)
            {
                best = new FzfMatchResult(0, 0, 0);
                return true;
            }
        }

        return false;
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
