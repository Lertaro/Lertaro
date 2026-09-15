using Lertaro.Core.SearchIndex.Fzf;

namespace Lertaro.Core.SearchIndex;

// A standalone, public entry point for the exact "name, falling back to alias" matching rule the core
// index scan already applies per record (see RecordSearch/CacheExtensions.cs and its siblings), for
// callers that need identical matching semantics without running an actual index scan -- e.g. a query
// token provider filtering already-fetched results by fzf pattern against something other than a
// record's own name (a path segment, say). FzfPattern itself stays internal; this is the one seam meant
// to cross the assembly boundary (see PluginSdk.Services.FuzzyMatchService, wired to this in
// PluginManager).
public static class FuzzyMatcher
{
    public static bool IsMatch(string pattern, string text)
    {
        if (string.IsNullOrEmpty(pattern) || string.IsNullOrEmpty(text))
            return false;

        var fzf = ParseQuery(pattern);

        // A query with nothing left to compare against (empty, or nothing but an operator) must not fall
        // into FzfPattern.TryMatchSingle's own "no term sets to check" -> true shortcut, which would
        // otherwise match every candidate.
        return !fzf.IsEmpty && IsMatch(fzf, text);
    }

    // The term operators the parser no longer produces, understood anyway for callers building a
    // pattern from a caller-supplied user string. The file-search pipeline strips its own operator
    // syntax (later tokens, the `\`/`<`/`>` token triggers) BEFORE the pattern ever gets here, so a
    // character that reaches this seam is a literal by the time it arrives -- which is why this is a
    // separate, opt-in entry point and not part of FzfPattern.Parse. Plugin catalogs, bookmark titles
    // and similar free-standing text have no such pipeline in front of them and keep the operators.
    // ponytail: five shapes, no nesting, no escaping. Widen the grammar only for a caller that needs it.
    private static bool TryParseOperators(string pattern, out FzfPattern fzf)
    {
        fzf = FzfPatternShapeExtensions.Empty;
        var text = pattern.Trim();
        if (text.Length == 0)
            return false;

        var fuzzyEnabled = SearchContext.FuzzyMatchEnabled;
        var sets = new List<FzfTermSet>();
        foreach (var raw in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (raw.Length == 0)
                continue;

            var kind = fuzzyEnabled ? FzfTermKind.Fuzzy : FzfTermKind.Exact;
            var inverse = false;
            var body = raw;

            if (body.StartsWith("!", StringComparison.Ordinal))
            {
                inverse = true;
                kind = FzfTermKind.Exact;
                body = body[1..];
            }

            if (body != "$" && body.EndsWith("$", StringComparison.Ordinal))
            {
                kind = FzfTermKind.Suffix;
                body = body[..^1];
            }

            if (body.Length > 2 && body.StartsWith("'", StringComparison.Ordinal) && body.EndsWith("'", StringComparison.Ordinal))
            {
                kind = FzfTermKind.ExactBoundary;
                body = body[1..^1];
            }
            else if (body.StartsWith("'", StringComparison.Ordinal))
            {
                // The quote FLIPS exactness, and a suffix anchor already owns the kind.
                if (kind != FzfTermKind.Suffix)
                    kind = fuzzyEnabled && !inverse ? FzfTermKind.Exact : FzfTermKind.Fuzzy;
                body = body[1..];
            }
            else if (body.StartsWith("^", StringComparison.Ordinal))
            {
                kind = kind == FzfTermKind.Suffix ? FzfTermKind.Equal : FzfTermKind.Prefix;
                body = body[1..];
                if (body.StartsWith("'", StringComparison.Ordinal))
                    body = body[1..];
            }

            if (body.Length == 0)
                continue;

            // One set PER WORD: space-separated words stay a conjunction (each its own 1-term set) while a
            // word's alias spellings remain OR alternatives inside that word's set -- the same shape
            // FzfPatternParser builds, which is why the expansion is reused rather than re-derived. Note a
            // word typed in full pinyin also needs the provider's CJK query forms to match a Chinese name.
            var lower = body.ToLowerInvariant();
            var terms = new List<FzfTerm> { new(kind, inverse, lower, CaseSensitive: false) };
            if (!inverse)
                FzfPatternParser.AddAliasQueryForms(terms, lower, kind);
            sets.Add(new FzfTermSet(terms.ToArray()));
        }

        if (sets.Count == 0)
            return false;

        fzf = FzfPatternShapeExtensions.FromTermSets(sets.ToArray());
        return true;
    }

    // The one query parser for every raw-string entry point on this seam. They must all read a query the
    // same way: if IsMatch honoured the term operators while the highlight/rank paths went through
    // FzfPattern.Parse (which no longer does), a caller could get "matched" together with an empty mask --
    // a row that lights nothing on the very characters the match was made of. A query that cannot be
    // parsed becomes the shared empty pattern, which every entry point already treats as "no match".
    private static FzfPattern ParseQuery(string query)
        => TryParseOperators(query, out var fzf) ? fzf : FzfPatternShapeExtensions.Empty;

    // Overload for callers that already hold the parsed pattern -- avoids re-parsing (and re-running
    // every registered alias provider's GetQueryForms) per candidate when the same query is tested
    // against many texts. The empty-pattern guard above stays on the string overload: a caller holding
    // a pattern is responsible for its own empty-pattern semantics, which differ per call site.
    internal static bool IsMatch(FzfPattern fzf, string text)
    {
        if (fzf.IsEmpty || string.IsNullOrEmpty(text))
            return false;
        if (fzf.TryMatch(text, out _, FzfScoringScheme.Default))
            return true;

        if (!AliasProviderRegistry.HasNonAscii(text))
            return false;

        var disabledIds = SearchContext.DisabledAliasIds;
        var queryLen = fzf.GetTotalTermLength();

        foreach (var provider in AliasProviderRegistry.GetActiveProviders())
        {
            if (disabledIds != null && disabledIds.Contains(AliasProviderRegistry.GetProviderId(provider)))
                continue;

            if (!provider.CanHandle(text))
                continue;

            foreach (var alias in provider.GetAliases(text))
            {
                if (!fzf.TryMatch(alias, out var aliasMatch, FzfScoringScheme.Default))
                    continue;

                // A precise query must not match a full transliteration mid-syllable -- see
                // AliasMatchRules, and the same check the core index scan applies.
                if (!AliasMatchRules.AllowsMatch(fzf.RequiresAlignedAliases, provider.SyllableSeparator, alias, aliasMatch.MinBegin))
                    continue;

                // Same quality bar the core index scan applies to its own alias fallback (see
                // FzfPattern.IsAcceptableAliasMatch) -- reject a match whose span is disproportionately
                // wider than the query, or whose score is too low, so a weak coincidental alias hit
                // doesn't count as a match here either.
                if (!fzf.IsAcceptableAliasMatch(aliasMatch, queryLen, alias, FzfScoringScheme.Default))
                    continue;

                return true;
            }
        }

        // Mixed-alphabet fallback (a bare term mixing a native-script character with alias-initial
        // letters, matched against a candidate starting with that same character) -- mirrors the
        // equivalent tier added to SearchMatcher/SearchMatcherRow so this public seam keeps matching
        // the host's own file search.
        // TrySegmentPattern already excluded a disabled provider from consideration.
        var mixedTerm = MixedQueryMatcher.TrySegmentPattern(fzf);
        if (mixedTerm != null && mixedTerm.Provider.CanHandle(text))
        {
            foreach (var aliasGroup in mixedTerm.Provider.GetAliases(text))
            {
                if (string.IsNullOrEmpty(aliasGroup))
                    continue;
                foreach (var segment in aliasGroup.Split('|'))
                {
                    if (segment.Length == 0)
                        continue;
                    if (MixedQueryMatcher.TryMatch(mixedTerm, text.AsSpan(), text, segment, out _))
                        return true;
                }
            }
        }

        return false;
    }

    // The public seam for HighlightMask's "final highlight result" mask -- used by App's
    // TextHighlighter for display, and mirrors exactly what the ranking weight (SearchMatcher's
    // FzfResultRank/FzfBytePattern.ForDefaultScheme) scores against for the same (text, query) pair.
    public static bool[] ComputeHighlightMask(string text, string query)
    {
        if (string.IsNullOrEmpty(text))
            return Array.Empty<bool>();
        if (string.IsNullOrEmpty(query))
            return new bool[text.Length];

        return ComputeHighlightMask(text, ParseQuery(query));
    }

    // Parsed-pattern overload: callers that test MANY texts against ONE query hit this instead of the
    // string overload, which re-parses (and re-runs every alias provider's GetQueryForms) per text.
    internal static bool[] ComputeHighlightMask(string? text, FzfPattern pattern)
        => string.IsNullOrEmpty(text) ? Array.Empty<bool>() : HighlightMask.Compute(text, pattern);

    // The same percentage*consecutiveness ranking weight the file-search hot path uses (see
    // FzfResultRank.ApplyWeight), exposed for callers outside Core that rank their own candidates by
    // something other than a raw fzf score -- e.g. SearchableItemMapper's plugin-provided catalog
    // items (System Settings, Start Menu apps, ...), which previously only bucketed by match kind
    // (prefix/contains/alias) with no notion of "how good" a match is within a bucket. Always computed
    // against `text` itself (never an intermediate alias string), matching what TextHighlighter shows.
    public static double ComputeMatchWeight(string text, string query)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(query))
            return 0;

        return HighlightMask.ComputeWeight(text, ParseQuery(query));
    }

    // The same "does it match, where does it start, how well" measure the two windows rank by -- start
    // position first, then weight (see MatchRank). Exposed alongside ComputeMatchWeight so callers that
    // only need the weight are not forced to pay for the extra field.
    public static MatchRank ComputeMatchRank(string text, string query)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(query))
            return MatchRank.NoMatch;

        return HighlightMask.ComputeRank(text, ParseQuery(query));
    }

    // Parsed-pattern overload -- same reason as ComputeHighlightMask's: the per-candidate string form
    // re-parses the query on every call, which measured over 6x slower across a candidate set once an
    // alias provider (pinyin) is registered, because Parse consults every provider's GetQueryForms.
    internal static MatchRank ComputeMatchRank(string? text, FzfPattern pattern)
        => string.IsNullOrEmpty(text) ? MatchRank.NoMatch : HighlightMask.ComputeRank(text, pattern);

    // The standard "does this match, and how well" contract for App-side candidate sources that judge
    // a match against more than one text representation of the same item -- a favorite's display name
    // and its path, a searchable item's title and its curated aliases, and so on. Checks `primaryText`
    // first, then each of `alternateTexts` in order, taking the strongest match (see IsStronger); mirrors
    // HighlightMask never scoring an intermediate alias string higher than what actually produced the
    // match. Every candidate source should call this rather than re-deriving its own literal-substring/
    // DP-fallback matching, so a multi-word query is always split into independently-required terms the
    // same way Core's own file search splits it.
    public static MatchRank ComputeBestMatch(string query, string primaryText, IEnumerable<string>? alternateTexts = null)
    {
        if (string.IsNullOrEmpty(query))
            return MatchRank.NoMatch;

        return ComputeBestMatch(ParseQuery(query), primaryText, alternateTexts);
    }

    // Parsed-pattern overload: this is the shape a per-candidate catalog scan uses (SearchableItemMapper
    // over every Start Menu/settings entry, HistorySearchCandidateMapper over the history list), and each
    // candidate can hand several texts (name plus aliases). The string overload used to re-parse the same
    // query once per IsMatch AND again per ComputeRank per text -- 2+ parses per text, thousands of texts
    // per keystroke. One parse, reused across every text and candidate, is what this overload removes.
    internal static MatchRank ComputeBestMatch(FzfPattern pattern, string? primaryText, IEnumerable<string>? alternateTexts = null)
    {
        var best = MatchRank.NoMatch;

        if (!string.IsNullOrEmpty(primaryText) && IsMatch(pattern, primaryText))
            best = Hit(pattern, primaryText);

        if (alternateTexts != null)
        {
            foreach (var text in alternateTexts)
            {
                if (string.IsNullOrEmpty(text) || !IsMatch(pattern, text))
                    continue;
                var rank = Hit(pattern, text);
                if (IsStronger(rank, best))
                    best = rank;
            }
        }

        return best;
    }

    // Stronger match wins, in the same order the windows rank by (see SearchResultRelevance): start
    // position first, then quality weight, then tier last. Tier being weakest means 英文 > 简拼 > 全拼 only
    // separates texts that already agree on where and how tightly they matched -- a tighter full-pinyin hit
    // beats a looser literal one, which is the requested global weighting.
    internal static bool IsStronger(MatchRank candidate, MatchRank incumbent)
    {
        if (!candidate.IsMatch)
            return false;
        if (!incumbent.IsMatch)
            return true;

        if (candidate.Start != incumbent.Start)
            return candidate.Start < incumbent.Start;
        if (candidate.Weight != incumbent.Weight)
            return candidate.Weight > incumbent.Weight;
        return candidate.Tier < incumbent.Tier;
    }

    // A text already known to match: rank it without re-testing the match.
    private static MatchRank Hit(FzfPattern pattern, string text) => HighlightMask.ComputeRank(text, pattern);
}
