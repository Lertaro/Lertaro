namespace Lertaro.Core.SearchIndex.Fzf;

// Alias-fallback quality-gating (IsAcceptableAliasMatch/WeightAliasMatch and their private helpers)
// lives in FzfPatternAliasMatchExtensions.cs (extension methods, matching TreeBuilder's Checkpoint/Diff
// split and MenuBuilder's ContentExtensions split) instead of a partial class, to keep this file under
// the project's line limit. Pattern parsing is delegated to FzfPatternParser for the same reason; this
// file keeps the immutable pattern state and core text-matching algorithm.
internal sealed class FzfPattern
{
    internal FzfPattern(string? targetDrive, FzfTermSet[] termSets)
        : this(targetDrive, termSets, null)
    {
    }

    // orGroups is the AND-first reading of the same query: a disjunction of conjunctions (DNF), where
    // each group contains ANDed term sets and each set retains its OR aliases. It is null whenever the flat termSets
    // already say the same thing, which is every query without a '|' plus every OR-first query -- the
    // hot engine keeps consuming TermSets unchanged and only a genuinely mixed AND-first query pays for
    // the extra shape. See FzfPatternParser.ParseTermSets.
    internal FzfPattern(string? targetDrive, FzfTermSet[] termSets, FzfTermGroup[]? orGroups)
    {
        TargetDrive = targetDrive;
        TermSets = termSets;
        OrGroups = orGroups;
        EffectiveSets = orGroups == null ? termSets : Flatten(orGroups);
    }

    public string? TargetDrive { get; }
    public FzfTermSet[] TermSets { get; }

    // Non-null only for an AND-first query that actually mixes '|' with spaces. When set, this is the
    // authoritative shape and TryMatch/TryMatchSingle evaluate it instead of TermSets.
    public FzfTermGroup[]? OrGroups { get; }

    // Whichever shape actually governs matching: OrGroups when the query is an AND-first mix, else the
    // flat TermSets. Everything that only needs to ENUMERATE the terms (alignment requirement, typed
    // length, alias gating) reads this rather than choosing between the two shapes itself.
    internal FzfTermSet[] EffectiveSets { get; }

    private static FzfTermSet[] Flatten(FzfTermGroup[] groups)
    {
        var count = 0;
        foreach (var group in groups)
            count += group.Sets.Length;
        var sets = new FzfTermSet[count];
        var index = 0;
        foreach (var group in groups)
            foreach (var set in group.Sets)
                sets[index++] = set;
        return sets;
    }

    public bool IsEmpty => TermSets.Length == 0;

    // True when every term was matched as a PRECISE run rather than as a scattered subsequence -- the
    // ordinary "fuzzy matching is switched off" query, and also an all-explicit-operator one. Alias
    // fallback then has to respect the provider's syllable boundaries (see AliasMatchRules), which is what
    // stops "ex" being read as the tail of "xue" plus the head of "xi".
    //
    // Keyed off the terms rather than SearchContext.FuzzyMatchEnabled so a term that explicitly flips
    // itself back to a subsequence ("'jtqin", under fuzzy-off) stays exempt: for that term the user did ask
    // for a loose match, and applying the boundary rule would contradict what the operator means.
    public bool RequiresAlignedAliases
    {
        get
        {
            foreach (var set in EffectiveSets)
            {
                foreach (var term in set.Terms)
                {
                    if (term.Inverse)
                        continue;
                    if (term.Kind == FzfTermKind.Fuzzy)
                        return false;
                }
            }
            return true;
        }
    }

    // How much text the user actually typed, which is what the alias-fallback quality gate scales its
    // thresholds against (see IsAcceptableAliasMatch). A term set holds ALTERNATIVES -- one OR branch,
    // or one of the spellings an alias provider offers for the same term -- so only one of them can
    // ever be what was typed, and only one is counted.
    //
    // Summing them instead made the gate reject genuine matches as soon as a term had several
    // alternatives: "jiating" expands to six pinyin readings, which inflated the length from 7 to 64
    // and pushed the required score past anything a real match scores, so 家庭... stopped being found
    // while the shorter "jiatin" (four readings) still squeaked through.
    //
    // An AND-first mix takes the longest GROUP, not the sum of every group: its groups are OR
    // alternatives, so including them all would scale the gate against branches the user's single query
    // can never require at once. Inside the winning group the terms DO all have to match, so they add up.
    public int GetTotalTermLength()
    {
        if (OrGroups != null)
        {
            var groupLen = 0;
            foreach (var group in OrGroups)
                groupLen = Math.Max(groupLen, SumPositiveTermLength(group.Sets));
            return groupLen;
        }

        return SumPositiveTermLength(TermSets);
    }

    private static int SumPositiveTermLength(FzfTermSet[] sets)
    {
        var len = 0;
        foreach (var set in sets)
            len += SumPositiveTermLength(set);
        return len;
    }

    private static int SumPositiveTermLength(FzfTermSet set)
    {
        foreach (var term in set.Terms)
        {
            if (term.Inverse)
                continue;
            return term.Text.Length; // the rest of this set are alternative spellings of the same typed text
        }
        return 0;
    }

    public static FzfPattern Parse(string query) => FzfPatternParser.Parse(query);

    public static FzfPattern ParseText(string query) => FzfPatternParser.ParseText(query);

    // One already-parsed term set lifted into a pattern of its own, so a caller can ask "which
    // candidates satisfy THIS term" instead of only "which satisfy the whole query". Reuses the parsed
    // term verbatim rather than re-parsing its text, which would have to re-derive kind/case-sensitivity
    // from a string the operators were already stripped from.
    internal static FzfPattern ForTermSet(FzfPattern source, int index)
        => new(source.TargetDrive, new[] { source.TermSets[index] });
    public bool TryMatch(ReadOnlySpan<char> text, out FzfPatternResult result, FzfScoringScheme scheme, FzfSlab? slab = null)
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

                if (TryMatchSingle(text.Slice(start, len), out var segmentResult, scheme, slab))
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

        return TryMatchSingle(text, out result, scheme, slab);
    }

    // Text never contains '|' here: the segmented branch above slices it away, and real file names
    // can't contain it (invalid in Windows paths) -- so no cross-'|' span check is needed.
    private bool TryMatchSingle(ReadOnlySpan<char> text, out FzfPatternResult result, FzfScoringScheme scheme, FzfSlab? slab = null)
    {
        // AND-first query that mixes '|' with spaces: a disjunction of AND-groups. Each group's term
        // sets retain the OR relationship between the typed term and its provider aliases.
        if (OrGroups != null)
        {
            foreach (var group in OrGroups)
            {
                if (TryMatchGroup(group, text, out result, scheme, slab))
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
            if (!TryMatchSet(set, text, out var best, scheme, slab))
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
    private bool TryMatchGroup(FzfTermGroup group, ReadOnlySpan<char> text, out FzfPatternResult result, FzfScoringScheme scheme, FzfSlab? slab)
    {
        var totalScore = 0;
        var minBegin = int.MaxValue;
        var minEnd = int.MaxValue;
        var maxEnd = 0;
        var validOffsetFound = false;

        foreach (var set in group.Sets)
        {
            if (!TryMatchSet(set, text, out var current, scheme, slab))
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
    private bool TryMatchSet(FzfTermSet set, ReadOnlySpan<char> text, out FzfMatchResult best, FzfScoringScheme scheme, FzfSlab? slab)
    {
        best = default;
        foreach (var term in set.Terms)
        {
            var current = FzfAlgorithm.Match(term.Kind, text, term.Text, term.CaseSensitive, scheme, slab);
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
}
