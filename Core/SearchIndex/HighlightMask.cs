using Lertaro.Core.SearchIndex.Fzf;
using Lertaro.PluginSdk.Abstractions.Plugins;

namespace Lertaro.Core.SearchIndex;

// The single "final highlight result" computation, shared by App's display highlighting
// (TextHighlighter, via FuzzyMatcher.ComputeHighlightMask) and Core's ranking weight (below) -- same
// per-term fallback: literal substring (every occurrence, for display) first, then the real
// FuzzyMatchV2 backtrace run directly against the text itself (covers a plain scattered/non-contiguous
// match with zero alias involvement, e.g. "chwx" against "China_White_X" -- previously the single
// biggest cost here, since it used to fall all the way to a DP re-derivation for this very common
// case), then a cheap greedy subsequence search against each alias-provider alias, mapped back onto the
// source text (covers a CJK name matched purely through pinyin -- kept as a plain scan rather than the
// real backtrace because a polyphonic name can expand to dozens of alias candidates and a synthetic
// pinyin string has no word-boundary structure worth the real algorithm's bonus scoring; measured
// slower overall to pay its DP cost that many times per candidate for no real accuracy gain).
internal static class HighlightMask
{
    // One reusable DP scratch buffer per thread (mirrors SearchMatcher's per-worker Slab) -- a fresh
    // FzfSlab starts with zero-length backing arrays, so allocating a new one per Compute/ComputeWeight
    // call would re-grow every array on its very first use and gain nothing; caching it per thread lets
    // repeated calls across many candidates (NameSearch's bounded refinement loop, PathGate's per-
    // segment weight, ...) reuse the same already-grown buffers instead of re-allocating every time.
    [ThreadStatic]
    private static FzfSlab? _threadSlab;

    private static FzfSlab RentSlab() => _threadSlab ??= new FzfSlab();

    public static bool[] Compute(string fullText, FzfPattern pattern)
    {
        var highlights = new bool[fullText.Length];
        if (fullText.Length == 0)
            return highlights;

        var materialized = fullText;
        Mark(fullText, pattern, highlights, ref materialized, RentSlab());
        return highlights;
    }

    // Ranking-facing: same computation, but works directly off a char span -- the (common) literal and
    // direct-fuzzy tiers never materialize a string at all; a string is only built if some term needs
    // the alias-provider tier, which requires the AliasProviderRegistry/IAliasProvider string APIs.
    public static double ComputeWeight(ReadOnlySpan<char> fullText, FzfPattern pattern)
    {
        if (fullText.Length == 0)
            return 0;

        var marks = fullText.Length <= 512 ? stackalloc bool[fullText.Length] : new bool[fullText.Length];
        marks.Clear();
        string? materialized = null;
        Mark(fullText, pattern, marks, ref materialized, RentSlab());
        return ComputeWeightFromMarks(marks);
    }

    private static void Mark(ReadOnlySpan<char> fullText, FzfPattern pattern, Span<bool> highlights, ref string? materialized, FzfSlab slab)
    {
        foreach (var set in pattern.TermSets)
        {
            // Highlight EVERY non-inverse term in the set that actually matches this candidate, not
            // just whichever one happens to be tried first -- a candidate containing more than one of a
            // multi-term OR set's terms (e.g. "我爱我家" containing both "我" and "爱" from "我 | 爱 |
            // 你") shows the union of all of them, matching what a user scanning the OR query visually
            // expects to see lit up, not just an arbitrary single winner.
            foreach (var term in set.Terms)
            {
                // An alias provider's rewriting of the user's term is for matching only. Its text is
                // the provider's internal shape (pinyin plus syllable boundaries), which appears
                // nowhere in the candidate, so the subsequence search below would spread it across the
                // whole name and light up characters the user never described -- searching a folder by
                // the pinyin of its first four characters lit up two more from the middle of the name.
                // The typed term still reaches the same aliases through MarkViaAliasProviders.
                if (term.Inverse || term.AliasForm)
                    continue;

                MarkTerm(fullText, term.Text, term.CaseSensitive, term.Kind, highlights, ref materialized, slab);
            }
        }
    }

    private static void MarkTerm(ReadOnlySpan<char> fullText, string term, bool caseSensitive, FzfTermKind kind, Span<bool> highlights, ref string? materialized, FzfSlab slab)
    {
        var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        if (MarkLiteralSpan(fullText, term, comparison, highlights))
            return;

        // Scattered positions are only ever what a fuzzy term matched on. Every other kind is built on
        // FzfExactMatcher, which is IndexOf under the same two StringComparisons the literal pass above
        // already used -- so if one of those matched at all, that pass found it, and reaching here means
        // it did not match this text. Running the fuzzy search anyway lit characters the term had
        // nothing to do with, and worse, returned before the alias tier below could be tried: a pinyin
        // term that happened to be a subsequence of some Latin run in a long path was answered by that
        // run instead of by the alias it actually matched.
        if (kind == FzfTermKind.Fuzzy &&
            FzfPositionMatcher.FuzzyMatchV2WithPositions(fullText, term, caseSensitive, FzfScoringScheme.Default, highlights, slab).IsMatch)
        {
            return;
        }

        materialized ??= fullText.ToString();
        if (MarkViaAliasProviders(materialized, term, caseSensitive, kind, highlights))
            return;

        MarkViaMixedQuery(materialized, term, caseSensitive, highlights);
    }

    private static bool MarkLiteralSpan(ReadOnlySpan<char> haystack, ReadOnlySpan<char> needle, StringComparison comparison, Span<bool> highlights)
    {
        if (needle.Length == 0)
            return false;

        var foundAny = false;
        var startIdx = 0;
        while (startIdx < haystack.Length)
        {
            var idx = haystack.Slice(startIdx).IndexOf(needle, comparison);
            if (idx < 0)
                break;

            var absolute = startIdx + idx;
            for (var i = absolute; i < absolute + needle.Length && i < highlights.Length; i++)
                highlights[i] = true;

            foundAny = true;
            startIdx = absolute + 1;
        }

        return foundAny;
    }

    // Corrected ranking-weight formula (percentage of the WHOLE candidate string that's covered,
    // then weighted by how contiguous that coverage is): weight = percentage * consecutiveness.
    // Both factors are <= 1, so this only ever demotes a match relative to its raw score -- it's a
    // ranking multiplier, never a gate; a candidate that already passed the real fzf match always
    // stays a match regardless of this weight.
    private static double ComputeWeightFromMarks(ReadOnlySpan<bool> mask)
    {
        if (mask.Length == 0)
            return 0;

        var matchedLength = 0;
        var sumOfSquares = 0L;
        var runLength = 0;
        foreach (var m in mask)
        {
            if (m)
            {
                matchedLength++;
                runLength++;
            }
            else if (runLength > 0)
            {
                sumOfSquares += (long)runLength * runLength;
                runLength = 0;
            }
        }
        if (runLength > 0)
            sumOfSquares += (long)runLength * runLength;

        if (matchedLength == 0)
            return 0;

        var percentage = (double)matchedLength / mask.Length;
        var consecutiveness = (double)sumOfSquares / ((long)matchedLength * matchedLength);
        return percentage * consecutiveness;
    }

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

    private static bool MarkViaAliasProviders(string text, string term, bool caseSensitive, FzfTermKind kind, Span<bool> highlights)
    {
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
                                : FindContiguousPositions(aliasLower, probe);
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
    // plain-alias tier above and this term's own literal/direct-fuzzy tiers have failed. Segments the term by an
    // active provider's own InputRanges/OutputRanges and, on a genuine mix, paints via
    // MixedQueryMatcher -- see its header comment for the run-by-run algorithm.
    private static void MarkViaMixedQuery(string text, string term, bool caseSensitive, Span<bool> highlights)
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

    // Finds ANY valid subsequence alignment of `term` within `text`, returning the matched positions in
    // `text` in order, or null if no such subsequence exists. Greedy (always takes the earliest possible
    // next position), which is enough for a highlight/weight mask -- this doesn't need the optimal/
    // highest-scoring alignment, just a real one.
    private static int[]? FindSubsequencePositions(string text, string term)
    {
        if (term.Length == 0)
            return null;

        var positions = new int[term.Length];
        var searchFrom = 0;
        for (var i = 0; i < term.Length; i++)
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
    // Only the first occurrence is reported: the mask is a union anyway, so every occurrence is an
    // equally good explanation of the same hit.
    private static int[]? FindContiguousPositions(string text, string term)
    {
        if (term.Length == 0)
            return null;
        var idx = text.IndexOf(term, StringComparison.Ordinal);
        if (idx < 0)
            return null;

        var positions = new int[term.Length];
        for (var i = 0; i < term.Length; i++)
            positions[i] = idx + i;
        return positions;
    }
}
