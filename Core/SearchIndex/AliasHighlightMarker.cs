using Lertaro.Core.SearchIndex.Fzf;
using Lertaro.PluginSdk.Abstractions.Plugins;

namespace Lertaro.Core.SearchIndex;

// The alias-provider tier of HighlightMask's "final highlight result": when a term matches a candidate
// only through an alias provider (a CJK name found by its pinyin, or a query mixing a native-script
// character with alias-initial letters), the matched positions have to be mapped back onto the source
// text before they can be painted. Split into its own class (composition, not a partial class) to keep
// HighlightMask under the repository's per-file limit; this class has no state of its own, it always
// operates on the text/highlight span its caller passes in.
internal static class AliasHighlightMarker
{
    // Map alias hits back to source text with the same provider/segment traversal as FuzzyMatcher.IsMatch.
    // Use a greedy earliest-position subsequence scan: aliases can expand to 32 combinations, and their
    // synthetic spellings gain little from FuzzyMatchV2's word-boundary bonuses.
    //
    // Try the typed term and that provider's query forms only against its own aliases. Forms may include
    // syllable separators; MapAliasToSourceIndices maps those matches back to the original name.
    // Cache forms by term and provider so ranking thousands of candidates does not repeat segmentation.
    [ThreadStatic]
    private static Dictionary<(IAliasProvider Provider, string Term, bool CaseSensitive), string[]>? _probeCache;

    private static string[] ProbesFor(IAliasProvider provider, string termLower, bool caseSensitive)
    {
        var cache = _probeCache ??= new Dictionary<(IAliasProvider, string, bool), string[]>();
        var key = (provider, termLower, caseSensitive);
        if (cache.TryGetValue(key, out var cached))
            return cached;

        var probes = new List<string> { termLower };
        foreach (var form in provider.GetQueryForms(termLower))
        {
            if (!string.IsNullOrEmpty(form))
                probes.Add(caseSensitive ? form : form.ToLowerInvariant());
        }

        // Bounded rather than grown forever: a session types a lot of distinct terms, and only the
        // handful in the query being ranked right now is ever read again.
        if (cache.Count >= 64)
            cache.Clear();
        return cache[key] = probes.ToArray();
    }

    // Reports the tier the match came from through `tier`, so ranking and highlighting agree on HOW a
    // candidate was found as well as on what lights up.
    //
    // precise is the query's own alignment rule (FzfPattern.RequiresAlignedAliases): a precise term may only
    // be painted where matching would have accepted it, i.e. on a syllable boundary.
    public static bool MarkViaAliasProviders(string text, string term, bool caseSensitive, FzfTermKind kind, bool precise, Span<bool> highlights, out int tier)
    {
        tier = MatchRank.TierFull;
        var termLower = caseSensitive ? term : term.ToLowerInvariant();
        // Both of the ways a provider can be switched off, because neither works in both processes.
        // GetActiveProviders consults a filter that reads the user's settings, which only the UI process
        // can do -- the service runs under an account whose LocalApplicationData is not the user's, so
        // it sees an empty settings file and considers everything enabled. What reaches the service is
        // the per-request id set below, carried over the pipe. Matching already honours that set (it
        // reads the ids baked into the snapshot); this, which generates aliases from the provider
        // directly, did not -- so a disabled provider still shaped the ranking weight and lit up
        // characters in the result the user never typed.
        var disabledIds = SearchContext.DisabledAliasIds;

        foreach (var provider in AliasProviderRegistry.GetActiveProviders())
        {
            var matchedAny = false;
            try
            {
                if (disabledIds != null && disabledIds.Contains(AliasProviderRegistry.GetProviderId(provider)))
                    continue;

                if (!provider.CanHandle(text))
                    continue;

                var separator = provider.SyllableSeparator;
                var probes = ProbesFor(provider, termLower, caseSensitive);

                foreach (var aliasGroup in provider.GetAliases(text))
                {
                    if (string.IsNullOrEmpty(aliasGroup))
                        continue;

                    foreach (var alias in aliasGroup.Split('|'))
                    {
                        if (string.IsNullOrEmpty(alias))
                            continue;

                        var aliasLower = caseSensitive ? alias : alias.ToLowerInvariant();
                        // Highlight according to each term's matching mode; the apostrophe operator can
                        // invert the global fuzzy setting. Try contiguous aliases before subsequences and
                        // select a tight occurrence using AliasMatchRules, so the mask reflects the
                        // occurrence accepted by matching.
                        int[]? positions = null;
                        foreach (var probe in probes)
                        {
                            positions = AliasHighlightPositions.ForProbe(aliasLower, probe, separator, kind, precise);
                            if (positions != null)
                                break;
                        }
                        if (positions == null)
                            continue;

                        var map = provider.MapAliasToSourceIndices(text, alias);
                        if (map == null || map.Length != alias.Length)
                            continue;

                        foreach (var aliasPos in positions)
                        {
                            if (aliasPos < 0 || aliasPos >= map.Length)
                                continue;
                            var sourceIndex = map[aliasPos];
                            if (sourceIndex >= 0 && sourceIndex < highlights.Length)
                                highlights[sourceIndex] = true;
                        }

                        matchedAny = true;
                        tier = Math.Min(tier, AliasMatchRules.TierFor(separator, matchedName: false, aliasLower.AsSpan()));
                    }
                }
            }
            catch
            {
                // Best-effort; fall through to the next provider rather than let one plugin's failure
                // block highlighting entirely.
            }

            if (matchedAny)
                return true;
        }

        return false;
    }

    // Mixed-alphabet fallback (a query mixing a native-script character with alias-initial letters,
    // matched against a candidate starting with that same character): only reached once both the
    // plain-alias tier above and the term's own literal/direct-fuzzy tiers have failed. Takes the
    // already-segmented term rather than segmenting it here, because the gate on WHICH term may use this
    // tier belongs to the pattern, not to the word -- see MixedQueryMatcher.TrySegmentPattern and its note
    // that a term inside a richer query is deliberately ineligible. Paints via MixedQueryMatcher; see its
    // header comment for the run-by-run algorithm.
    public static void MarkViaMixedQuery(string text, MixedTerm mixedTerm, Span<bool> highlights)
    {
        if (!mixedTerm.Provider.CanHandle(text))
            return;

        // This lane matches an alias as text and maps the positions it took back onto the source, so the
        // spelling comes from the provider itself (AppendSpellings) -- its readings are decided live and
        // nothing bakes them; the alias strings added below are the ones that are not spellings of those
        // readings.
        var segments = new List<string>(4);
        if (mixedTerm.Provider is IAliasLiveMatcherProvider liveProvider)
            liveProvider.AppendSpellings(text, segments);

        foreach (var aliasGroup in mixedTerm.Provider.GetAliases(text))
        {
            if (string.IsNullOrEmpty(aliasGroup))
                continue;

            foreach (var alias in aliasGroup.Split('|'))
            {
                if (alias.Length > 0)
                    segments.Add(alias);
            }
        }

        foreach (var segment in segments)
        {
            if (MixedQueryMatcher.TryMatchAndHighlight(mixedTerm, text, segment, highlights))
                return;
        }
    }
}

