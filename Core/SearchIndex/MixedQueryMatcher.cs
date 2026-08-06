using Lertaro.Core.SearchIndex.Fzf;
using Lertaro.PluginSdk.Abstractions.Plugins;

namespace Lertaro.Core.SearchIndex;

// Matches a query term that mixes an alias provider's own literal source alphabet (InputRanges,
// e.g. CJK) with that same provider's generated-alias alphabet (OutputRanges, e.g. pinyin letters) --
// e.g. one leading native-script character followed by a few alias-initial letters, matched against a
// candidate whose own text starts with that same character: the leading character must match the
// candidate's own text literally, the alias-initial letters must match a subsequence of the provider's
// baked alias for the candidate's REMAINING (not yet consumed) characters. Reached only as a
// last-resort tier, after the ordinary whole-query-against-name and whole-query-against-alias attempts
// both fail -- a plain literal or single-alphabet query never reaches this file.
internal enum MixedRunKind { Literal, AliasSyntax }

internal readonly record struct MixedRun(MixedRunKind Kind, string Text);

internal sealed class MixedTerm
{
    public required IAliasProvider Provider { get; init; }
    public required byte ProviderId { get; init; }
    public required MixedRun[] Runs { get; init; }
}

internal readonly record struct MixedMatchResult(int Score, int MinBegin, int MaxEnd, bool ValidOffsetFound);

internal static class MixedQueryMatcher
{
    // Every matched char scores the same flat bonus -- this tier only needs to clear
    // FzfPattern.IsAcceptableAliasMatch's queryLen*5 floor and rank sensibly against other mixed
    // candidates; it doesn't feed into the same DP the real fzf scorer uses, so there's nothing to
    // stay bit-compatible with.
    private const int ScorePerChar = 16;

    // Only a bare single fuzzy term (typed as-is, no "!", "^", "$", "'", "|", multi-word) is
    // eligible -- segmenting a term that's part of a richer query (multiple space-separated terms,
    // an OR-set, an inverse/exact/prefix modifier) would require deciding how the mixed sub-match
    // combines with the rest of the query's own semantics, which this tier doesn't attempt.
    public static MixedTerm? TrySegmentPattern(FzfPattern pattern)
    {
        if (pattern.TermSets.Length != 1)
            return null;
        var terms = pattern.TermSets[0].Terms;
        if (terms.Length != 1)
            return null;
        var term = terms[0];
        if (term.Inverse || term.Kind != FzfTermKind.Fuzzy || term.CaseSensitive)
            return null;

        return TrySegment(term.Text);
    }

    // Disabled providers are excluded from the candidate pool up front (not just rejected after a
    // successful segmentation) -- SearchContext.DisabledAliasIds doesn't change mid-scan (set once per
    // search request, read here via AsyncLocal before the parallel candidate scan begins), so filtering
    // here is exact, not a race. This matters once more than one provider is registered: a disabled
    // provider whose ranges happen to also fit the term must never take the slot a still-enabled
    // provider's ranges would also have matched.
    public static MixedTerm? TrySegment(string term)
    {
        var disabledIds = SearchContext.DisabledAliasIds;
        foreach (var provider in AliasProviderRegistry.GetActiveProviders())
        {
            var providerId = AliasProviderRegistry.GetProviderId(provider);
            if (disabledIds != null && disabledIds.Contains(providerId))
                continue;

            var runs = SegmentFor(term, provider);
            if (runs != null)
                return new MixedTerm { Provider = provider, ProviderId = providerId, Runs = runs };
        }
        return null;
    }

    // Splits `term` into contiguous runs by which of the provider's declared ranges each char falls
    // in. Returns null unless the term contains BOTH a literal-range run and an alias-range run --
    // a term entirely within one provider's own literal-range alphabet is not a mix and must fall
    // through to the ordinary literal/whole-alias tiers unchanged, otherwise a coincidental short
    // alias subsequence can false-positive match unrelated candidates. A char outside both ranges
    // also disqualifies the provider entirely (this term isn't expressible as a mix of this
    // provider's two alphabets).
    private static MixedRun[]? SegmentFor(string term, IAliasProvider provider)
    {
        var hasLiteral = false;
        var hasAlias = false;
        var runs = new List<MixedRun>();
        var start = 0;
        MixedRunKind? currentKind = null;

        for (var i = 0; i <= term.Length; i++)
        {
            MixedRunKind? kind = null;
            if (i < term.Length)
            {
                kind = Classify(term[i], provider);
                if (kind == null)
                    return null;
            }

            if (kind != currentKind)
            {
                if (currentKind != null)
                    runs.Add(new MixedRun(currentKind.Value, term.Substring(start, i - start)));
                start = i;
                currentKind = kind;
            }

            if (kind == MixedRunKind.Literal) hasLiteral = true;
            else if (kind == MixedRunKind.AliasSyntax) hasAlias = true;
        }

        return hasLiteral && hasAlias ? runs.ToArray() : null;
    }

    private static MixedRunKind? Classify(char c, IAliasProvider provider)
    {
        foreach (var (s, e) in provider.InputRanges)
            if (c >= s && c <= e) return MixedRunKind.Literal;
        foreach (var (s, e) in provider.OutputRanges)
            if (c >= s && c <= e) return MixedRunKind.AliasSyntax;
        return null;
    }

    // `name` and `nameForMap` are the same text (span + string form, so callers that already have
    // one or the other don't need to re-materialize). `aliasSegment` is a single reading (caller has
    // already split any '|'-joined alternatives).
    public static bool TryMatch(MixedTerm term, ReadOnlySpan<char> name, string nameForMap, string aliasSegment, out MixedMatchResult result)
    {
        var positions = SharedPositions;
        positions.Clear();
        if (!TryMatchCore(term, name, nameForMap, aliasSegment, positions))
        {
            result = default;
            return false;
        }

        var minBegin = int.MaxValue;
        var maxEnd = 0;
        foreach (var p in positions)
        {
            if (p < minBegin) minBegin = p;
            if (p + 1 > maxEnd) maxEnd = p + 1;
        }

        result = new MixedMatchResult(positions.Count * ScorePerChar, minBegin, maxEnd, positions.Count > 0);
        return true;
    }

    public static bool TryMatchAndHighlight(MixedTerm term, string name, string aliasSegment, Span<bool> highlights)
    {
        var positions = SharedPositions;
        positions.Clear();
        if (!TryMatchCore(term, name, name, aliasSegment, positions))
            return false;

        foreach (var p in positions)
            if (p >= 0 && p < highlights.Length)
                highlights[p] = true;
        return true;
    }

    // Reused per call on the same thread; each caller clears it before use and never lets it escape.
    [ThreadStatic] private static List<int>? _sharedPositions;
    private static List<int> SharedPositions => _sharedPositions ??= new List<int>(16);

    // Walks the term's runs in order against `name`, threading a monotonically-advancing source-index
    // cursor so later runs can never match characters an earlier run already consumed (preserves the
    // order the user typed them in). A Literal run must appear as a literal substring at/after the
    // cursor. An AliasSyntax run must appear as a subsequence of `aliasSegment`, restricted to the
    // alias positions whose MapAliasToSourceIndices source index is at/after the cursor. On success,
    // every matched source character index (from both kinds of run) is appended to `positionsOut`.
    private static bool TryMatchCore(MixedTerm term, ReadOnlySpan<char> name, string nameForMap, string aliasSegment, List<int> positionsOut)
    {
        var map = term.Provider.MapAliasToSourceIndices(nameForMap, aliasSegment);
        if (map == null || map.Length != aliasSegment.Length)
            return false;

        var cursor = 0;
        foreach (var run in term.Runs)
        {
            if (run.Kind == MixedRunKind.Literal)
            {
                if (run.Text.Length == 0 || cursor > name.Length - run.Text.Length)
                    return false;
                var idx = name.Slice(cursor).IndexOf(run.Text, StringComparison.OrdinalIgnoreCase);
                if (idx < 0)
                    return false;
                var absStart = cursor + idx;
                for (var i = absStart; i < absStart + run.Text.Length; i++)
                    positionsOut.Add(i);
                cursor = absStart + run.Text.Length;
            }
            else
            {
                var searchFrom = 0;
                while (searchFrom < map.Length && map[searchFrom] < cursor)
                    searchFrom++;

                var runPositions = FindSubsequencePositions(aliasSegment, run.Text, searchFrom);
                if (runPositions == null)
                    return false;

                var maxSource = cursor;
                foreach (var aliasPos in runPositions)
                {
                    var sourceIndex = map[aliasPos];
                    positionsOut.Add(sourceIndex);
                    if (sourceIndex + 1 > maxSource) maxSource = sourceIndex + 1;
                }
                cursor = maxSource;
            }
        }
        return true;
    }

    // Earliest-position greedy subsequence search, starting no earlier than `searchFrom` -- mirrors
    // HighlightMask's own FindSubsequencePositions (same "a real alignment, not the optimal one, is
    // enough" tradeoff), with the extra start bound this tier needs to keep alias-run matches from
    // reaching back before the cursor.
    private static int[]? FindSubsequencePositions(string text, string term, int searchFrom)
    {
        if (term.Length == 0)
            return null;

        var positions = new int[term.Length];
        var from = searchFrom;
        for (var i = 0; i < term.Length; i++)
        {
            var idx = text.IndexOf(term[i], from);
            if (idx < 0)
                return null;
            positions[i] = idx;
            from = idx + 1;
        }
        return positions;
    }
}
