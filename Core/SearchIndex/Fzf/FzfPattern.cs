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
        HasExclusions = EffectiveSets.Any(set => set.Terms.Any(term => term.Inverse));
    }

    // The same query as the ALIAS tiers read it: every positive term offered as a contiguous run first and
    // as a subsequence second. Built on demand -- a candidate set with no aliases never pays for it -- and
    // kept here so it is built once per query rather than once per candidate. The construction (and why
    // both readings are needed, in that order) lives in FzfPatternAliasReading.
    private FzfPattern? _aliasReading;
    internal FzfPattern AliasReading => _aliasReading ??= FzfPatternAliasReading.Build(this);

    // This pattern read by the byte-side matchers (see FzfBytePattern), built on demand for the same reason
    // as AliasReading: the tiers that reach it already hold a pattern, not a second pre-encoded object, and
    // one pattern serves every candidate of a query.
    private FzfBytePattern? _byteView;
    internal FzfBytePattern ByteView => _byteView ??= FzfBytePattern.From(this);

    public string? TargetDrive { get; }
    public FzfTermSet[] TermSets { get; }

    // Whether the query holds a `!term`. Exclusions only ever have something to compare against in a
    // candidate's own name (see ExclusionsPass), so the alias tiers test this before they run and skip
    // the check entirely for the far more common query that has none.
    public bool HasExclusions { get; }

    // Whether any set has a positive term at all. A query made only of `!term`s matches every candidate
    // that lacks the excluded text but has nothing to paint; callers use this to give it a synthetic rank
    // instead of mistaking the empty mask for a failed match.
    public bool HasPositiveTerms => EffectiveSets.Any(set => set.Terms.Any(term => !term.Inverse));

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

    // Whether an alias match has to respect the provider's syllable boundaries (see AliasMatchRules), which
    // is what stops "ex" being read as the tail of "xue" plus the head of "xi", and "huchu" as the middle of
    // 出's "chu" followed by 出 again.
    //
    // Always on, in both modes. A transliteration is a SPELLING of the candidate rather than the text the
    // user typed, and no spelling of a sound starts in the middle of that sound: "从某个字的中间开始" is
    // never what an abbreviation means, so fuzzy matching must not license it. The user asked for exactly
    // that (pinyin search behaving the same whether or not fuzzy matching is on), and the earlier split --
    // exact mode aligning, fuzzy mode loose -- made the same query reach a file in one mode and not the
    // other. What still differs by mode is the CANDIDATE's own text: exact mode wants a run in the name,
    // fuzzy mode accepts a scattered subsequence of it.
    public bool RequiresAlignedAliases => true;

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
