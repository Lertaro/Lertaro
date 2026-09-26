using Lertaro.Core.SearchIndex.Query;

namespace Lertaro.Core.SearchIndex.Fzf;

// Alias-fallback quality-gating (IsAcceptableAliasMatch/WeightAliasMatch and their private helpers) lives
// in FzfPatternAliasMatchExtensions.cs and the pattern factories (Empty/FromTermSets) in
// FzfPatternShapeExtensions.cs -- extension methods, not partials, to keep this file under the project's
// line limit. Pattern parsing is delegated to FzfPatternParser for the same reason; this file keeps the
// immutable pattern state and core text-matching algorithm.
internal sealed class FzfPattern
{
    internal FzfPattern(string? targetDrive, FzfTermSet[] termSets)
        : this(targetDrive, termSets, null)
    {
    }

    // orGroups is the AND-first reading of the same query: a disjunction of conjunctions (DNF), where
    // each group contains ANDed term sets and each set retains its OR aliases. It is null whenever the flat
    // termSets already say the same thing, which is every query without a '|' plus every OR-first query --
    // the hot engine keeps consuming TermSets unchanged and only a genuinely mixed AND-first query pays for
    // the extra shape. See FzfPatternParser.ParseTermSets.
    internal FzfPattern(string? targetDrive, FzfTermSet[] termSets, FzfTermGroup[]? orGroups)
        : this(targetDrive, termSets, orGroups, null)
    {
    }

    // regexes are the "/.../" clauses from the query, ANDed with everything else. They cannot ride in
    // TermSets because the byte-level matcher (FzfBytePattern) has no regex support and the char-level
    // TermSets have no notion of a pattern that is not fixed text -- so they are carried alongside and
    // applied by TryMatchSingle on the decoded char span (see RegexClauses).
    internal FzfPattern(string? targetDrive, FzfTermSet[] termSets, FzfTermGroup[]? orGroups, string[]? regexes)
    {
        TargetDrive = targetDrive;
        TermSets = termSets;
        OrGroups = orGroups;
        Regexes = regexes;
        EffectiveSets = orGroups == null ? termSets : Flatten(orGroups);
        HasPositiveTerm = AnyPositiveTerm(EffectiveSets);
        HasExclusions = AnyInverseTerm(EffectiveSets);
        RequiredRegexLiteral = ComputeRequiredRegexLiteral(regexes);
    }

    public string? TargetDrive { get; }
    public FzfTermSet[] TermSets { get; }

    // Non-null only for an AND-first query that actually mixes '|' with spaces. When set, this is the
    // authoritative shape and TryMatch/TryMatchSingle evaluate it instead of TermSets.
    public FzfTermGroup[]? OrGroups { get; }

    // Non-null only when the query carried one or more "/.../" clauses. Every clause must match for
    // the whole pattern to match.
    internal string[]? Regexes { get; }

    // The longest run of literal characters that every regex clause requires a matching name to contain,
    // or empty when no clause has one (an alternation, a bare wildcard, a class-only pattern...). This is
    // what lets a regex search feed the index's character-mask prefilter: the clause itself is far too
    // expensive to run per candidate, and without a literal there is nothing cheap to reject on.
    //
    // Computed once per parsed query rather than per search, because the mask is built per query and the
    // extraction walks the pattern. This is the ONLY extraction: RegexQueryParser produces the clause
    // patterns and nothing else, so the walk happens here and nowhere earlier.
    // Empty rather than null so callers can test it without a null check.
    // RegexLiteralExtractor guarantees the literal really is required of EVERY match -- a wrong one would
    // silently drop results rather than merely miss an optimization, which is why its rules are stated in
    // that file.
    internal string RequiredRegexLiteral { get; }

    private static string ComputeRequiredRegexLiteral(string[]? regexes)
    {
        if (regexes is not { Length: > 0 })
            return string.Empty;

        var best = string.Empty;
        foreach (var pattern in regexes)
        {
            var literal = RegexLiteralExtractor.ExtractRequiredLiteral(pattern);
            // Every clause must match, so the longest single-run requirement wins; the caller ORs the
            // per-clause masks together, which keeps a clause with no literal from disabling the others.
            if (literal.Length > best.Length)
                best = literal;
        }

        return best;
    }

    // Whichever shape actually governs matching: OrGroups when the query is an AND-first mix, else the
    // flat TermSets. Everything that only needs to ENUMERATE the terms (alignment requirement, typed
    // length, alias gating) reads this rather than choosing between the two shapes itself.
    internal FzfTermSet[] EffectiveSets { get; }

    // False when every term is an exclusion (":temp", ":temp :log"). An exclusion-only query has nothing
    // to match ON -- an exclusion can only remove candidates from a set some positive term produced -- so
    // such a pattern must match NOTHING rather than everything. It would otherwise match everything: an
    // inverse term is satisfied by the ABSENCE of its text, so a lone ":temp" leaves every candidate
    // satisfying it and the user would see the whole index after typing a filter. Decided here (rather
    // than in the parser) because the DNF shape is materialized in this constructor too.
    internal bool HasPositiveTerm { get; }

    private static bool AnyPositiveTerm(FzfTermSet[] sets)
    {
        foreach (var set in sets)
        {
            foreach (var term in set.Terms)
            {
                if (!term.Inverse)
                    return true;
            }
        }
        return false;
    }

    // True when the query filters with at least one ":term". Exclusions are a statement about the
    // candidate's NAME, so a caller matching an alias has to keep reading the name for them -- and a
    // caller whose matcher cannot do that at all (FzfBytePattern, which has no bytes for a non-ASCII
    // exclusion) must step aside instead of answering on the exclusion's behalf. See
    // FzfPatternMatcher.TryMatchAlias and SearchMatcherAliasExtensions.
    internal bool HasExclusions { get; }

    private static bool AnyInverseTerm(FzfTermSet[] sets)
    {
        foreach (var set in sets)
        {
            foreach (var term in set.Terms)
            {
                if (term.Inverse)
                    return true;
            }
        }
        return false;
    }

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

    // True when the pattern has nothing to match ON -- no ordinary terms AND no regex clauses. Callers use
    // this to mean "there is no query here", so a regex-only pattern must NOT report empty: it has real
    // matching to do, just not through a term. It used to count term sets alone, which made
    // "/\.exe$/" look like an empty query -- NameSearch's drive gate rejected it outright and the
    // search returned nothing at all, and FuzzyMatcher would have called the empty pattern a non-match.
    public bool IsEmpty => TermSets.Length == 0 && Regexes is not { Length: > 0 };

    // True when every term has to be matched as a PRECISE run rather than as a scattered subsequence --
    // the ordinary "fuzzy matching is switched off" query. Alias fallback then has to respect the
    // provider's syllable boundaries (see AliasMatchRules), which is what stops "ex" being read as the
    // tail of "xue" plus the head of "xi".
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
    // thresholds against (see IsAcceptableAliasMatch). A term set holds ALTERNATIVES -- one OR branch, or
    // one of the spellings an alias provider offers for the same term -- so only one of them can ever be
    // what was typed, and only one is counted. Summing them instead made the gate reject genuine matches:
    // "jiating" expands to six pinyin readings, inflating the length from 7 to 64 and pushing the required
    // score past anything a real match scores. An AND-first mix takes the longest GROUP for the same reason
    // -- its groups are OR alternatives, so counting them all would scale the gate against branches the
    // user's single query can never require at once. Inside the winning group the terms DO all have to
    // match, so they add up.
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

    internal static FzfPattern Parse(string query, string[]? regexes) => FzfPatternParser.Parse(query, regexes);

    public static FzfPattern ParseText(string query) => FzfPatternParser.ParseText(query);

    internal static FzfPattern ParseText(string query, string[]? regexes) =>
        FzfPatternParser.ParseText(query, regexes);

    // One already-parsed term set lifted into a pattern of its own, so a caller can ask "which
    // candidates satisfy THIS term" instead of only "which satisfy the whole query". Reuses the parsed
    // term verbatim rather than re-parsing its text, which would have to re-derive kind/case-sensitivity
    // from a string the operators were already stripped from.
    internal static FzfPattern ForTermSet(FzfPattern source, int index)
        => new(source.TargetDrive, new[] { source.TermSets[index] });

    // The matching itself lives in FzfPatternMatcher (a static helper taking this pattern as a per-call
    // parameter), split out to keep this file under the repository's per-file line limit. This stays the
    // entry point every consumer already calls.
    public bool TryMatch(ReadOnlySpan<char> text, out FzfPatternResult result, FzfScoringScheme scheme, FzfSlab? slab = null)
        => FzfPatternMatcher.TryMatch(this, text, out result, scheme, slab);

    /// <summary>
    /// Matches a candidate's <paramref name="alias"/> (a pinyin/simplified spelling a provider baked
    /// for <paramref name="name"/>) against this pattern. Every positive term reads the alias, but an
    /// exclusion always reads <paramref name="name"/>: ":term" states what the candidate's NAME must
    /// not contain, and the alias is exactly the text that cannot be expected to carry it.
    /// </summary>
    /// <remarks>
    /// Callers that hold the name anyway (every alias-fallback tier does) must use this instead of
    /// TryMatch(alias): the plain overload would evaluate the exclusion against the alias, where it is
    /// absent by construction, and so hand back a file the user explicitly excluded. Pass the name
    /// itself as <paramref name="name"/> when the candidate has no separate alias text.
    /// </remarks>
    public bool TryMatchAlias(ReadOnlySpan<char> alias, ReadOnlySpan<char> name, out FzfPatternResult result, FzfScoringScheme scheme, FzfSlab? slab = null)
        => FzfPatternMatcher.TryMatchAlias(this, alias, name, out result, scheme, slab);
}
