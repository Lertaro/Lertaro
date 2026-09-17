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
    // paying its full DP cost per candidate measured slower overall than this simpler scan.
    //
    // "Simpler" is not "earliest possible", though -- see FindSubsequencePositions, which tries each start
    // and keeps the tightest alignment. A plain leftmost-greedy walk measurably does NOT come out the same:
    // it spread "wangfei" across 我愿 before reaching the 王菲 that actually matched it.
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
    public static bool MarkViaAliasProviders(string text, string term, bool caseSensitive, FzfTermKind kind, Span<bool> highlights, out int tier)
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
                        // Follow the same rule matching does -- which is this TERM's kind, not the
                        // fuzzy setting. Reading the setting instead was right until a "'" was
                        // involved, since that flips one term's exactness against it: with fuzzy off,
                        // "'abc" searches as a subsequence but was highlighted as a contiguous run,
                        // found nothing, and lit up nothing at all while the row itself was a hit.
                        //
                        // Contiguous for every other kind, because a scattered subsequence lights up
                        // characters that had nothing to do with the hit: "gsh" matches 格式化 through
                        // the initials alias, but a subsequence search also finds g...s...h spread
                        // across the full pinyin and lit 创 along with it.
                        int[]? positions = null;
                        foreach (var probe in probes)
                        {
                            positions = kind == FzfTermKind.Fuzzy
                                ? FindSubsequencePositions(aliasLower, probe)
                                : FindContiguousPositions(aliasLower, probe, separator);
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
    // plain-alias tier above and the term's own literal/direct-fuzzy tiers have failed. Segments the term
    // by an active provider's own InputRanges/OutputRanges and, on a genuine mix, paints via
    // MixedQueryMatcher -- see its header comment for the run-by-run algorithm.
    public static void MarkViaMixedQuery(string text, string term, bool caseSensitive, Span<bool> highlights)
    {
        if (caseSensitive)
            return;

        var mixedTerm = MixedQueryMatcher.TrySegment(term);
        if (mixedTerm == null || !mixedTerm.Provider.CanHandle(text))
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

    // Finds a subsequence alignment of `term` within `text`, returning the matched positions in order, or
    // null if there is none.
    //
    // The START is not fixed at the first occurrence, and that is the whole point. A walk that always takes
    // the earliest possible position smears a term that really belongs to one later part of the alias across
    // everything in front of it: "wangfei" against "wo|yuanyi|yi|-|wang|fei" takes its 'w' from 我 and its
    // 'a','n' from 愿 before it ever reaches 王菲, so a candidate matched on 王菲 alone lights 我愿 as well --
    // characters the user never typed, and a mask that disagrees with the match that produced the row.
    //
    // So every occurrence of the term's first character is tried and the TIGHTEST alignment wins. That is
    // what the real fzf backtrace would have answered here anyway (its consecutive-match bonus makes the
    // late, near-contiguous run the best-scoring one), without paying for the backtrace per alias: a
    // polyphonic name expands to dozens of alias candidates, and this runs once per candidate.
    //
    // ponytail: at most `MaxSubsequenceStarts` starts are tried and a fully contiguous run ends the search
    // early, so an alias made almost entirely of one common letter cannot turn this into a quadratic scan.
    // A term whose tightest alignment is still loose gets a real, not an optimal, one -- fine for a mask.
    private const int MaxSubsequenceStarts = 16;

    private static int[]? FindSubsequencePositions(string text, string term)
    {
        if (term.Length == 0)
            return null;

        int[]? best = null;
        var bestSpan = int.MaxValue;
        var nextStart = 0;

        for (var attempt = 0; attempt < MaxSubsequenceStarts; attempt++)
        {
            var first = text.IndexOf(term[0], nextStart);
            if (first < 0)
                break;
            nextStart = first + 1;

            var positions = WalkFrom(text, term, first);
            if (positions == null)
                continue;

            var span = positions[^1] - positions[0];
            if (span >= bestSpan)
                continue;

            best = positions;
            bestSpan = span;

            // Fully contiguous: no alignment anywhere in the string can be tighter.
            if (span == term.Length - 1)
                break;
        }

        return best;
    }

    // One leftmost-greedy walk from a fixed first position: every later character takes the earliest
    // position after the previous one. Null as soon as a character runs out.
    private static int[]? WalkFrom(string text, string term, int first)
    {
        var positions = new int[term.Length];
        positions[0] = first;
        var searchFrom = first + 1;

        for (var i = 1; i < term.Length; i++)
        {
            var idx = text.IndexOf(term[i], searchFrom);
            if (idx < 0)
                return null;
            positions[i] = idx;
            searchFrom = idx + 1;
        }

        return positions;
    }

    // Contiguous counterpart of the walk above, for when matching itself demands a contiguous run.
    //
    // Occurrences are scanned until one starts on a syllable boundary (see AliasMatchRules.IsBoundaryAligned).
    // Skipping a misaligned occurrence rather than giving up is deliberate: this side must never be
    // STRICTER than matching, or a row that matched would render with nothing highlighted. Matching
    // rejects only when the first occurrence it scored is misaligned, so accepting any aligned occurrence
    // here keeps the two in agreement for every realistic alias -- and errs toward lighting something up.
    private static int[]? FindContiguousPositions(string text, string term, char separator)
    {
        if (term.Length == 0)
            return null;

        var searchFrom = 0;
        while (searchFrom <= text.Length - term.Length)
        {
            var idx = text.IndexOf(term, searchFrom, StringComparison.Ordinal);
            if (idx < 0)
                return null;
            if (AliasMatchRules.IsBoundaryAligned(separator, text, idx))
            {
                var positions = new int[term.Length];
                for (var i = 0; i < term.Length; i++)
                    positions[i] = idx + i;
                return positions;
            }
            searchFrom = idx + 1;
        }

        return null;
    }
}
