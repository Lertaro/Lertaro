using Lertaro.Core.SearchIndex.Fzf;

namespace Lertaro.Core.SearchIndex;

// A standalone, public entry point for the exact "name, falling back to alias" matching rule the core
// index scan already applies per record (see RecordSearch/CacheExtensions.cs and its siblings), for
// callers that need identical matching semantics without running an actual index scan -- e.g. a query
// token provider filtering already-fetched results by fzf pattern against something other than a
// record's own name (a path segment, in PathExclusionQueryTokenProvider's case). FzfPattern itself stays
// internal; this is the one seam meant to cross the assembly boundary (see PluginSdk.Services.
// FuzzyMatchService, wired to this in PluginManager).
public static class FuzzyMatcher
{
    public static bool IsMatch(string pattern, string text)
    {
        if (string.IsNullOrEmpty(pattern) || string.IsNullOrEmpty(text))
            return false;

        var fzf = FzfPattern.Parse(pattern);

        // A pattern that's entirely a drive spec ("d:\") parses down to zero real search terms once
        // Parse strips the prefix into TargetDrive -- meaningful for the index/path searchers (IndexV2,
        // LiveDirectorySearcher), which track TargetDrive themselves and treat "no terms" as "list
        // everything on that drive". This seam has no such drive-scoped listing mode: it matches
        // free-standing text (app names, bookmark titles, ...) with no concept of a drive, so an empty
        // term set has nothing left to compare against and must not fall into FzfPattern.TryMatchSingle's
        // own "no term sets to check" -> true shortcut, which would otherwise match every candidate.
        if (fzf.IsEmpty)
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

        return HighlightMask.Compute(text, FzfPattern.Parse(query));
    }

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

        return HighlightMask.ComputeWeight(text, FzfPattern.Parse(query));
    }

    // The standard "does this match, and how well" contract for App-side candidate sources that judge
    // a match against more than one text representation of the same item -- a favorite's display name
    // and its path, a searchable item's title and its curated aliases, and so on. Checks `primaryText`
    // first, then each of `alternateTexts` in order, taking whichever matched with the highest weight
    // (mirrors HighlightMask never scoring an intermediate alias string higher than what actually
    // produced the match). Every candidate source should call this rather than re-deriving its own
    // literal-substring/DP-fallback matching, so a multi-word query is always split into independently-
    // required terms the same way Core's own file search splits it.
    public static (bool IsMatch, double Weight) ComputeBestMatch(string query, string primaryText, IEnumerable<string>? alternateTexts = null)
    {
        if (string.IsNullOrEmpty(query))
            return (false, 0);

        var isMatch = false;
        var weight = 0.0;

        if (!string.IsNullOrEmpty(primaryText) && IsMatch(query, primaryText))
        {
            isMatch = true;
            weight = ComputeMatchWeight(primaryText, query);
        }

        if (alternateTexts != null)
        {
            foreach (var text in alternateTexts)
            {
                if (string.IsNullOrEmpty(text) || !IsMatch(query, text))
                    continue;
                isMatch = true;
                weight = Math.Max(weight, ComputeMatchWeight(text, query));
            }
        }

        return (isMatch, weight);
    }
}
