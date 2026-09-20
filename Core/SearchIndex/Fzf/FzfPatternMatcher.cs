namespace Lertaro.Core.SearchIndex.Fzf;

// The matching half of FzfPattern: which terms a candidate satisfies, how they score, and where they land.
//
// Split out purely to keep FzfPattern under the repository's per-file line limit -- a static helper that
// takes the pattern as a per-call parameter (this codebase's convention for that split), holding no state of
// its own. FzfPattern keeps the immutable pattern state and the public TryMatch entry point, which delegates
// here.
internal static class FzfPatternMatcher
{
    // The name-tier entry point: the candidate's own name IS the text being matched, so an exclusion
    // reads that same span. See FzfPattern.TryMatch.
    public static bool TryMatch(FzfPattern pattern, ReadOnlySpan<char> text, out FzfPatternResult result, FzfScoringScheme scheme, FzfSlab? slab = null)
        => TryMatchCore(pattern, text, text, out result, scheme, slab);

    // The alias-tier entry point: every positive term reads `alias`, while an exclusion ALWAYS reads
    // `name`. ":term" says "the candidate's name does not contain this" -- and an alias (pinyin, a
    // simplified spelling) is precisely the string that cannot be expected to contain it, so matching
    // an exclusion against an alias silently admits every excluded file whose positive terms happen to
    // be reachable through an alias. See FzfPattern.TryMatchAlias.
    public static bool TryMatchAlias(FzfPattern pattern, ReadOnlySpan<char> alias, ReadOnlySpan<char> name, out FzfPatternResult result, FzfScoringScheme scheme, FzfSlab? slab = null)
        => TryMatchCore(pattern, alias, name, out result, scheme, slab);

    private static bool TryMatchCore(FzfPattern pattern, ReadOnlySpan<char> text, ReadOnlySpan<char> exclusionText, out FzfPatternResult result, FzfScoringScheme scheme, FzfSlab? slab = null)
    {
        if (text.Contains('|'))
        {
            // ponytail: handle polyphonic aliases by matching each segment independently to prevent
            // incorrect cross-boundary match failure. Slicing (not Substring) keeps this allocation-free.
            var bestResult = default(FzfPatternResult);
            var matchedAny = false;
            var start = 0;
            while (start < text.Length)
            {
                var len = text.Slice(start).IndexOf('|');
                if (len < 0)
                    len = text.Length - start;

                if (TryMatchSingle(pattern, text.Slice(start, len), exclusionText, out var segmentResult, scheme, slab))
                {
                    if (segmentResult.ValidOffsetFound)
                    {
                        segmentResult = new FzfPatternResult(
                            segmentResult.Score,
                            segmentResult.MinBegin + start,
                            segmentResult.MinEnd + start,
                            segmentResult.MaxEnd + start,
                            true
                        );
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

        return TryMatchSingle(pattern, text, exclusionText, out result, scheme, slab);
    }

    // Text never contains '|' here: the segmented branch above slices it away, and real file names
    // can't contain it (invalid in Windows paths) -- so no cross-'|' span check is needed.
    private static bool TryMatchSingle(FzfPattern pattern, ReadOnlySpan<char> text, ReadOnlySpan<char> exclusionText, out FzfPatternResult result, FzfScoringScheme scheme, FzfSlab? slab = null)
    {
        // An exclusion-only query matches nothing at all -- see HasPositiveTerm. Checked before the regex
        // clauses because it is a property of the query shape, not of this candidate, so no text can
        // change the answer.
        if (!pattern.HasPositiveTerm && pattern.Regexes is not { Length: > 0 })
        {
            result = default;
            return false;
        }

        // Regex clauses are ANDed with everything else and checked first: a miss here is a miss for the
        // whole pattern, and the regex engine is the most expensive step in the chain.
        if (pattern.Regexes is { Length: > 0 } regexes && !RegexClauses.AllMatch(regexes, text))
        {
            result = default;
            return false;
        }

        // Nothing left to combine -- a regex-only query (the name satisfied the regex; no text offsets to
        // report, since the regex's own match span is not tracked) or a bare drive spec. Either way the
        // prefilter has already done the work, so this is a match.
        if (pattern.TermSets.Length == 0 && pattern.OrGroups == null)
        {
            result = new FzfPatternResult(0, -1, -1, 0, false);
            return true;
        }

        // AND-first query that mixes '|' with spaces: a disjunction of AND-groups. Each group's term
        // sets retain the OR relationship between the typed term and its provider aliases.
        if (pattern.OrGroups != null)
        {
            foreach (var group in pattern.OrGroups)
            {
                if (TryMatchGroup(pattern, group, text, exclusionText, out result, scheme, slab))
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

        foreach (var set in pattern.TermSets)
        {
            if (!TryMatchSet(set, text, exclusionText, out var best, scheme, slab))
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

    // One AND-group of the DNF shape: every term set must be satisfied, while each set keeps its own OR
    // alternatives (including alias spellings).
    private static bool TryMatchGroup(FzfPattern pattern, FzfTermGroup group, ReadOnlySpan<char> text, ReadOnlySpan<char> exclusionText, out FzfPatternResult result, FzfScoringScheme scheme, FzfSlab? slab)
    {
        var totalScore = 0;
        var minBegin = int.MaxValue;
        var minEnd = int.MaxValue;
        var maxEnd = 0;
        var validOffsetFound = false;

        foreach (var set in group.Sets)
        {
            if (!TryMatchSet(set, text, exclusionText, out var current, scheme, slab))
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

    // The OR-alternatives-within-one-AND-condition semantics (mirrors FzfBytePattern.TryMatch's inner
    // loop), extracted so both the flat and the DNF paths share one implementation.
    private static bool TryMatchSet(FzfTermSet set, ReadOnlySpan<char> text, ReadOnlySpan<char> exclusionText, out FzfMatchResult best, FzfScoringScheme scheme, FzfSlab? slab)
    {
        best = default;
        var foundPositive = false;
        foreach (var term in set.Terms)
        {
            // An exclusion reads exclusionText -- the candidate's own name, which is `text` itself on the
            // name tier and the real name on the alias tier (see TryMatchAlias). Its OR partners in this
            // same set still read `text`, so "report | :temp" keeps meaning what it always did: a
            // candidate whose alias carries "report" is admitted even when its name carries "temp".
            var current = FzfAlgorithm.Match(term.Kind, term.Inverse ? exclusionText : text, term.Text, term.CaseSensitive, scheme, slab);
            if (term.Inverse)
            {
                // In an OR set, a negative alternative is satisfied when its text is absent. Do not
                // return false merely because it is present: a later positive alternative may still match.
                if (!current.IsMatch)
                    return true;
                continue;
            }

            if (current.IsMatch && (!foundPositive || current.Score > best.Score))
            {
                best = current;
                foundPositive = true;
            }
        }

        return foundPositive;
    }
}
