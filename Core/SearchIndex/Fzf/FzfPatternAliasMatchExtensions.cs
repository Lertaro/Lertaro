namespace Lertaro.Core.SearchIndex.Fzf;

// Validates and weights alias matches through FzfPattern's public TermSets and GetTotalTermLength APIs.
internal static class FzfPatternAliasMatchExtensions
{
    // Shared quality bar every alias-fallback caller applies: reject a match whose span is
    // disproportionately wider than the query, or whose score is too low, so a weak coincidental
    // alias hit doesn't count as a match.
    public static bool IsAcceptableAliasMatch(this FzfPattern pattern, FzfPatternResult aliasMatch) => pattern.IsAcceptableAliasMatch(aliasMatch, pattern.GetTotalTermLength());

    // Overload for a caller checking multiple alias matches against the same pattern (e.g. looping over
    // several alias providers/aliases per match attempt) -- GetTotalTermLength() only depends on the
    // pattern itself, so hoisting it once avoids recomputing it per alias.
    public static bool IsAcceptableAliasMatch(this FzfPattern pattern, FzfPatternResult aliasMatch, int queryLen)
    {
        // The tightness the painter applies (AliasHighlightPositions.Tightest -> AliasMatchRules.IsTightOccurrence),
        // so a row can no longer match on an alias occurrence the highlight layer refuses to paint: that
        // gap is what let a 3-letter exact query rank a file it merely scattered across (matching allowed
        // 20 characters of span, painting 12). Both sides measure an inclusive width: MaxEnd is exclusive.
        var span = aliasMatch.MaxEnd - aliasMatch.MinBegin;
        return AliasMatchRules.IsTightOccurrence(queryLen, span) && aliasMatch.Score >= queryLen * 5;
    }

    // If the combined-span gate fails, allow distant terms when each term set matches tightly and at
    // least one term has AliasFallbackAnchorLength characters. Short fragments alone remain subject
    // to the combined-span gate because they match long aliases too easily by chance.
    //
    // For example, "zg syd ebu" matches separated parts of "D_《中华人民共和国兽药典》2015年版二部" (#143).
    // About 3,600 adversarial cross-file queries measured 0.3–0.5% additional accepts, against the
    // combined gate's roughly 5% baseline. Use FzfAlgorithm.Match directly to avoid reparsing per term;
    // this fallback runs only after the combined gate fails.
    public static bool IsAcceptableAliasMatch(this FzfPattern pattern, FzfPatternResult aliasMatch, int queryLen, ReadOnlySpan<char> text, FzfScoringScheme scheme, FzfSlab? slab = null)
    {
        if (pattern.IsAcceptableAliasMatch(aliasMatch, queryLen))
            return true;

        // A query whose terms all have to be findable somewhere: one anchored term (3+ characters) is
        // required, so a bare crowd of 2-letter fragments -- cheap to match by coincidence anywhere in a
        // long alias -- stays excluded and keeps the combined-span check's protection.
        if (!pattern.HasAliasFallbackAnchorTerm())
            return false;

        var tightSumLimit = Math.Max(queryLen * AliasFallbackTightSumMultiplier, AliasFallbackTightSumFloor);

        // Mirror TryMatch's own '|' segment-splitting (polyphonic alias variants, e.g. 和's he/hu/huo
        // readings): every term set must land its own tight match within the SAME segment, not
        // scattered across the whole joined string -- otherwise a term from one reading's segment
        // could pair with another term from a DIFFERENT, mutually-exclusive reading's segment.
        var start = 0;
        while (start < text.Length)
        {
            var len = text.Slice(start).IndexOf('|');
            if (len < 0)
                len = text.Length - start;
            var segment = text.Slice(start, len);
            if (pattern.HasTightMatchInAnyGroup(segment, scheme, slab))
                return true;
            if (pattern.TightWindowsWithin(segment, tightSumLimit))
                return true;
            start += len + 1;
        }
        return false;
    }

    private static bool HasAliasFallbackAnchorTerm(this FzfPattern pattern)
    {
        foreach (var set in pattern.EffectiveSets)
            foreach (var term in set.Terms)
                if (!term.Inverse && term.Text.Length >= AliasFallbackAnchorLength)
                    return true;
        return false;
    }

    private static bool HasTightMatchInAnyGroup(this FzfPattern pattern, ReadOnlySpan<char> segment, FzfScoringScheme scheme, FzfSlab? slab)
    {
        if (pattern.OrGroups == null)
            return pattern.EveryTermSetHasTightMatch(pattern.TermSets, segment, scheme, slab);

        foreach (var group in pattern.OrGroups)
            if (pattern.EveryTermSetHasTightMatch(group.Sets, segment, scheme, slab))
                return true;
        return false;
    }

    private static bool EveryTermSetHasTightMatch(this FzfPattern pattern, FzfTermSet[] sets, ReadOnlySpan<char> segment, FzfScoringScheme scheme, FzfSlab? slab)
    {
        foreach (var set in sets)
        {
            var hasPositiveTerm = false;
            var setOk = false;
            foreach (var term in set.Terms)
            {
                if (term.Inverse)
                    continue; // An exclude term has no "own tight match" to require here -- the
                              // original TryMatch that produced `aliasMatch` already enforced it.
                hasPositiveTerm = true;

                var termResult = FzfAlgorithm.Match(term.Kind, segment, term.Text, term.CaseSensitive, scheme, slab);
                if (!termResult.IsMatch)
                    continue;

                var termSpan = termResult.End - termResult.Start;
                if (AliasMatchRules.IsTightOccurrence(term.Text.Length, termSpan))
                {
                    setOk = true;
                    break;
                }
            }

            if (hasPositiveTerm && !setOk)
                return false;
        }

        return true;
    }

    private const int AliasFallbackAnchorLength = 3;

    // Budget for the summed tight windows below, in units of what the user typed. Measured on the ticket
    // name (184 characters, a 303-character full alias): "fangdan baohu nx dnwwzz sj" -- five syllable
    // blocks from five different parts of the name -- sums to 51 against 22 typed characters, and
    // "fdbjf gao" sums to 17 against 8; 3x covers both with room for a long initials block, whose window
    // legitimately spans one syllable per letter.
    private const int AliasFallbackTightSumMultiplier = 3;
    private const int AliasFallbackTightSumFloor = 12;

    // Accepts when every positive term set has SOME tight occurrence in the segment and the total of
    // those occurrences stays proportionate to the typed length -- regardless of how far apart they sit.
    //
    // Why a sum of per-term windows rather than the envelope of one alignment: a query's syllables may
    // match anywhere in a long name (防弹 ... 保护 ... 电脑玩物站长), and an envelope grows with the NAME's
    // length while what the user typed does not. The sum only measures what each block costs to spell
    // out, so a query stays proportionate to its own length instead of to the gap between its first and
    // last block. It also uses each term's TIGHTEST alignment (AliasMatchRules.TightestOccurrence)
    // rather than the highest-scoring one fzf reports: boundary bonuses make the scoring search prefer a
    // wide alignment over a narrow one in an alias where every syllable starts on a delimiter.
    private static bool TightWindowsWithin(this FzfPattern pattern, ReadOnlySpan<char> segment, int limit)
    {
        var total = 0;
        foreach (var set in pattern.EffectiveSets)
        {
            var hasPositiveTerm = false;
            var best = -1;
            foreach (var term in set.Terms)
            {
                if (term.Inverse)
                    continue;
                hasPositiveTerm = true;
                var occurrence = AliasMatchRules.TightestOccurrence(segment, term.Text, term.CaseSensitive);
                if (occurrence == null)
                    continue;
                var window = occurrence[^1] - occurrence[0] + 1;
                if (best < 0 || window < best)
                    best = window;
            }

            if (!hasPositiveTerm)
                continue;
            if (best < 0)
                return false;

            total += best;
            if (total > limit)
                return false;
        }

        return true;
    }

    // Refines scores of accepted alias matches by queryLen / (MaxEnd - MinBegin). This penalizes gaps
    // inside the matched span while ignoring unused trailing alias text, so a tight initials hit can beat
    // a longer scattered hit (#89). It does not reject candidates and uses only precomputed fields,
    // making it cheap enough for the scan.
    public static FzfPatternResult WeightAliasMatch(this FzfPattern pattern, FzfPatternResult aliasMatch, int queryLen)
    {
        if (!aliasMatch.ValidOffsetFound)
            return aliasMatch;
        var span = aliasMatch.MaxEnd - aliasMatch.MinBegin;
        if (span <= queryLen)
            return aliasMatch;
        var weight = (double)queryLen / span;
        return aliasMatch with { Score = (int)Math.Round(aliasMatch.Score * weight) };
    }
}
