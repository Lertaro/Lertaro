using Lertaro.Core.IndexV2.Alias;
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
    // Mirrors FuzzyMatcher.IsMatch's own alias fallback (same provider iteration, same alias/'|'
    // segment structure), mapping the matched positions back onto `text` via
    // MapAliasToSourceIndices -- so a CJK name matched only through pinyin still highlights (and
    // scores) even though the query never appears verbatim in the original text. Uses a plain greedy
    // earliest-position subsequence search per alias rather than the real FuzzyMatchV2 backtrace:
    // a polyphonic CJK name can expand to dozens of alias candidates here (PinyinAliasProvider allows
    // up to 32 combinations), and unlike a real file/folder name a synthetic pinyin string has no
    // camelCase/word-boundary structure for the real algorithm's bonus scoring to add value from -- so
    // paying its full DP cost per candidate measured slower overall than this simpler scan, for a mask
    // that (per real name/text) comes out effectively identical either way.
    //
    // The typed term plus a provider's own spellings of it. The rewritten forms are what actually
    // appear in its aliases -- a term typed as one run of letters is not present verbatim in an alias
    // that marks syllable boundaries -- so leaving them out means a pinyin search highlights nothing at
    // all. They are only ever compared against THAT provider's aliases, and MapAliasToSourceIndices
    // translates whatever matches (boundary characters included) back onto the original text.
    //
    // Cached because they depend on the term and the provider and nothing else, while this is reached
    // once per CANDIDATE: ranking a CJK query re-segmented the same pinyin term for every one of the
    // thousands of candidates in the refinement set, which was most of what that refinement cost.
    [ThreadStatic]
    private static Dictionary<(IAliasProvider Provider, string Term, bool CaseSensitive), string[]>? _probeCache;

    // Scratch for the grid tier below: the encoded grid, its bound index, one reading choice per position
    // and the materialized alias.
    [ThreadStatic] private static AliasByteSink? _gridSink;
    [ThreadStatic] private static AliasGridFormat.Index? _gridIndex;
    [ThreadStatic] private static byte[]? _gridChoice;
    [ThreadStatic] private static char[]? _gridAlias;

    private static readonly AliasGridTier[] GridTiers = { AliasGridTier.Initials, AliasGridTier.Full };

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
                {                    if (string.IsNullOrEmpty(aliasGroup))
                        continue;

                    foreach (var alias in aliasGroup.Split('|'))
                    {
                        if (string.IsNullOrEmpty(alias))
                            continue;

                        var aliasLower = caseSensitive ? alias : alias.ToLowerInvariant();
                        // Follow the same rule matching does -- which is this TERM's kind, not the
                        // fuzzy setting. Reading the setting instead was right until a "'" was
                        // involved, since that flips one term's exactness against it: with fuzzy off,
                        // "'abc" searches as a subsequence but was highlighted as a contiguous run,
                        // found nothing, and lit up nothing at all while the row itself was a hit.
                        //
                        // Matching now reads a term against an alias BOTH ways (a contiguous run if it can
                        // be, a subsequence otherwise -- see FzfPatternAliasReading), so painting offers the
                        // same two, in the same order; anything stricter paints nothing on a row that did
                        // match, and an empty mask is not "no match" to every caller. The occurrence painted
                        // is the term's TIGHTEST one, not its earliest: the gate accepts a match when each
                        // term has a tight occurrence somewhere, so painting the greedy leftmost alignment
                        // instead lit characters the gate had not accepted (a 7-letter query lit six
                        // characters over 116 positions of a flattened initials alias).
                        // AliasMatchRules owns that measure so matching and painting agree.
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

                // One more tier for a grid-capable provider, and only when the string tier above painted
                // nothing: the grid witness materializes its own reading choice, which for a name whose
                // aliases ARE the enumerated strings lights a second, differently-wrong set of positions
                // on top of the ones the strings already explained (measured: a pinyin query for the
                // first two characters of a 184-character name lit four unrelated characters elsewhere in
                // it). The tier exists for the row whose aliases are STORED as a grid, where these
                // enumerated strings are the lossy single-reading form and may not match at all -- and
                // without it such a row is found by the engine and then renders as no match, because the
                // caller's final mask is what its item gate reads.
                if (!matchedAny && provider is IAliasGridProvider gridProvider)
                    matchedAny |= MarkViaGrid(gridProvider, text, probes, kind, precise, highlights, ref tier);
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

        foreach (var aliasGroup in mixedTerm.Provider.GetAliases(text))
        {
            if (string.IsNullOrEmpty(aliasGroup))
                continue;

            foreach (var alias in aliasGroup.Split('|'))
            {
                if (string.IsNullOrEmpty(alias))
                    continue;
                if (MixedQueryMatcher.TryMatchAndHighlight(mixedTerm, text, alias, highlights))
                    return;
            }
        }
    }

    // The reading-grid tier of the same alias fallback. A grid-capable provider's alias space is not
    // enumerable as strings, so the positions come from its readings instead -- the same witness the
    // search decides with, materialized into one alias string and then mapped back onto the source text.
    //
    // This tier is what keeps a grid-only hit from rendering as nothing: HighlightMask's final mask is
    // what the App's item gate reads, and an empty mask is NoMatch there. It needs no snapshot because it
    // encodes the grid from the provider for this one candidate, exactly like the string tier above
    // regenerates that provider's aliases live.
    private static bool MarkViaGrid(
        IAliasGridProvider provider,
        string text,
        string[] probes,
        FzfTermKind kind,
        bool precise,
        Span<bool> highlights,
        ref int tier)
    {
        var sink = _gridSink ??= new AliasByteSink();
        sink.Reset();
        if (AliasGridFormat.Encode(text, provider, sink) <= 0)
            return false;

        var grid = _gridIndex ??= new AliasGridFormat.Index();
        if (!grid.TryBind(sink.Segment(0)))
            return false;

        var separator = provider.SyllableSeparator;
        var positions = grid.PositionCount;
        if (positions == 0)
            return false;
        if (_gridChoice == null || _gridChoice.Length < positions)
            _gridChoice = new byte[Math.Max(positions, 64)];

        var matched = false;
        foreach (var probe in probes)
        {
            foreach (var readingTier in GridTiers)
            {
                Array.Clear(_gridChoice, 0, positions);
                if (!AliasGridMatcher.TryMatchTerm(grid, probe, kind, readingTier, separator, _gridChoice))
                    continue;

                var length = AliasGridMatcher.Measure(grid, readingTier, _gridChoice);
                if (length == 0)
                    continue;
                if (_gridAlias == null || _gridAlias.Length < length)
                    _gridAlias = new char[Math.Max(length, 64)];

                var written = AliasGridMatcher.Materialize(grid, readingTier, separator, _gridChoice, _gridAlias);
                var alias = new string(_gridAlias, 0, written);
                var aliasLower = alias.ToLowerInvariant();

                var found = AliasHighlightPositions.ForGridProbe(aliasLower, probe, separator, kind, precise);
                if (found == null)
                    continue;

                var map = provider.MapAliasToSourceIndices(text, alias);
                if (map == null || map.Length != alias.Length)
                    continue;

                foreach (var aliasPos in found)
                {
                    if (aliasPos < 0 || aliasPos >= map.Length)
                        continue;
                    var sourceIndex = map[aliasPos];
                    if (sourceIndex >= 0 && sourceIndex < highlights.Length)
                        highlights[sourceIndex] = true;
                }

                matched = true;
                tier = Math.Min(tier, AliasMatchRules.TierFor(separator, matchedName: false, aliasLower.AsSpan()));
            }
        }

        return matched;
    }
}

