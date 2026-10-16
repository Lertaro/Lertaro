using Lertaro.Core.SearchIndex.Fzf;

namespace Lertaro.Core.SearchIndex;

// The single "final highlight result" computation, shared by App's display highlighting
// (TextHighlighter, via FuzzyMatcher.ComputeHighlightMask) and Core's ranking measure (below) -- same
// per-term fallback: literal substring (every occurrence, for display) first, then the real
// FuzzyMatchV2 backtrace run directly against the text itself (covers a plain scattered/non-contiguous
// match with zero alias involvement, e.g. "chwx" against "China_White_X" -- previously the single
// biggest cost here, since it used to fall all the way to a DP re-derivation for this very common
// case), then the alias-provider tier (see AliasHighlightMarker), which covers a CJK name matched purely
// through pinyin and maps the matched positions back onto the source text.
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
        Mark(fullText, pattern, highlights, ref materialized, RentSlab(), out _);
        return highlights;
    }

    // Ranking-facing: same computation, but works directly off a char span -- the (common) literal and
    // direct-fuzzy tiers never materialize a string at all; a string is only built if some term needs
    // the alias-provider tier, which requires the AliasProviderRegistry/IAliasProvider string APIs.
    public static double ComputeWeight(ReadOnlySpan<char> fullText, FzfPattern pattern)
        => ComputeRank(fullText, pattern).Weight;

    // Match shape + tier + start position + quality weight from ONE mask pass. Every ranking window needs
    // all four (see MatchRank for their order), and computing them separately would build the same mask
    // several times, the alias path being the expensive one.
    public static MatchRank ComputeRank(ReadOnlySpan<char> fullText, FzfPattern pattern)
    {
        if (fullText.Length == 0)
            return MatchRank.NoMatch;

        var marks = fullText.Length <= 512 ? stackalloc bool[fullText.Length] : new bool[fullText.Length];
        marks.Clear();
        string? materialized = null;
        var tier = Mark(fullText, pattern, marks, ref materialized, RentSlab(), out var shape);
        var rank = RankFromMarks(marks);
        // The mask is a union across terms, so it cannot say HOW any one of them matched; Mark reports the
        // strongest tier any term reached and the folded shape, which is what decides the ordering.
        return rank.IsMatch ? rank with { Tier = tier, Shape = shape } : rank;
    }

    // Paints every matching term and returns the strongest tier reached (MatchRank.TierName .. TierFull).
    // A term satisfied by the candidate's own text outranks one satisfied through a provider's alias, so
    // the minimum wins; a multi-term query where one word is literal and another came from pinyin is
    // ranked as a literal hit, which is the evidence that actually matters to the user.
    //
    // Shape folds the other way round from tier, following the query's own logic: within a set (an OR
    // group) the best-shaped term counts, and across sets (AND) the worst set decides -- "code atlas"
    // against "code-atlasing" is only as good as its weaker word. A set with nothing marked (the match came
    // through a route the mask cannot paint) is treated as scattered rather than guessed better.
    private static int Mark(ReadOnlySpan<char> fullText, FzfPattern pattern, Span<bool> highlights, ref string? materialized, FzfSlab slab, out int shape)
    {
        var tier = MatchRank.TierFull;
        shape = MatchRank.ShapeWholeWord;
        foreach (var set in pattern.EffectiveSets)
        {
            var setShape = MatchRank.ShapeNoName;
            var hasPositiveTerm = false;
            // Highlight EVERY non-inverse term in the set that actually matches this candidate, not
            // just whichever one happens to be tried first -- a candidate containing more than one of a
            // multi-term OR set's terms (e.g. "我爱我家" containing both "我" and "爱" from "我 | 爱 |
            // 你") shows the union of all of them, matching what a user scanning the OR query visually
            // expects to see lit up, not just an arbitrary single winner.
            foreach (var term in set.Terms)
            {
                if (term.Inverse)
                    continue;
                hasPositiveTerm = true;

                // An alias provider's rewriting of the user's term is normally for matching only: its
                // text is the provider's internal shape (pinyin plus syllable boundaries), which appears
                // nowhere in the candidate, so the fuzzy walk in MarkTerm would spread it across the
                // whole name and light up characters the user never described -- searching a folder by
                // the pinyin of its first four characters lit up two more from the middle of the name.
                // The typed term still reaches those aliases through AliasHighlightMarker.
                //
                // A rewriting that IS literally present is the exception worth painting: the Simplified
                // spelling of a Traditional query ("網易" -> "网易") is real text sitting in the
                // candidate, and it can be the ONLY thing that matches. Skipping it left the mask empty,
                // and an empty mask is not "no match" to every caller: MarkTerm's alias tier cannot reach
                // this case either (that tier asks the provider for the CANDIDATE's aliases and maps them
                // back onto it, and a candidate that is already Simplified has none), so RankFromMarks
                // answered NoMatch and SearchableItemMapper's `match.IsMatch` gate dropped the row
                // outright. The file engine never showed this because it gates on the match itself, not
                // on this rank -- which is why a Traditional query found the file and not the app.
                //
                // Only the literal tier, never the fuzzy one: that is what keeps an internal spelling
                // from smearing.
                if (term.AliasForm)
                {
                    var aliasComparison = term.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
                    if (MarkLiteralSpan(fullText, term.Text, aliasComparison, highlights, out var aliasShape))
                    {
                        tier = Math.Min(tier, MatchRank.TierName);
                        setShape = Math.Min(setShape, aliasShape);
                    }
                    continue;
                }

                tier = Math.Min(tier, MarkTerm(fullText, term.Text, term.CaseSensitive, term.Kind, highlights, ref materialized, slab, pattern, out var termShape));
                setShape = Math.Min(setShape, termShape);
            }

            if (hasPositiveTerm)
                shape = Math.Max(shape, Math.Min(setShape, MatchRank.ShapeScattered));
        }
        return tier;
    }

    // Returns the tier of the strongest tier this term matched through: TierName for the candidate's own
    // text, otherwise the provider tier its alias supplied. `shape` is how the term sat in the text, or
    // ShapeNoName when it did not match.
    private static int MarkTerm(ReadOnlySpan<char> fullText, string term, bool caseSensitive, FzfTermKind kind, Span<bool> highlights, ref string? materialized, FzfSlab slab, FzfPattern pattern, out int shape)
    {
        var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        if (MarkLiteralSpan(fullText, term, comparison, highlights, out shape))
            return MatchRank.TierName;

        // The alias tier runs BEFORE the name's own scattered pass, because on a long name that pass
        // almost always succeeds by accident and paints the wrong characters: for a pinyin query "bi"
        // against a 184-character filename, "b" came from "chu ban" and "i" from "tai bei shi" while the
        // alias that explains the match (笔) painted nothing, because whichever tier returns first is the
        // only one that paints. A provider's own spellings are the meaningful explanation whenever they
        // exist; the name's scattered pass is the fallback for a query that no provider can express.
        materialized ??= fullText.ToString();
        shape = MatchRank.ShapeWordStart;
        if (AliasHighlightMarker.MarkViaAliasProviders(materialized, term, caseSensitive, kind, pattern.RequiresAlignedAliases, highlights, out var aliasTier))
            return aliasTier;

        // Scattered positions are only ever what a fuzzy term matched on. Every other kind is built on
        // FzfExactMatcher, which is IndexOf under the same two StringComparisons the literal pass above
        // already used -- so if one of those matched at all, that pass found it, and reaching here means
        // it did not match this text. Running the fuzzy search anyway lit characters the term had nothing
        // to do with.
        if (kind == FzfTermKind.Fuzzy &&
            FzfPositionMatcher.FuzzyMatchV2WithPositions(fullText, term, caseSensitive, FzfScoringScheme.Default, highlights, slab).IsMatch)
        {
            shape = MatchRank.ShapeScattered;
            return MatchRank.TierName;
        }

        // Keep the whole-query gate (a mixed term inside an AND/OR query is ineligible), but only
        // segment after the cheaper tiers fail. Doing this before them allocated per ranked/displayed
        // row even for a plain literal match, where no alias provider is needed at all.
        var mixedTerm = MixedQueryMatcher.TrySegmentPattern(pattern);
        if (mixedTerm != null)
            AliasHighlightMarker.MarkViaMixedQuery(materialized, mixedTerm, highlights);
        else
            shape = MatchRank.ShapeNoName;
        return MatchRank.TierFull;
    }

    // Paints every occurrence and reports the best shape among them (ShapeNoName when there is none), so
    // "metatlas_atlas" ranks by its whole-word "atlas", not by the first hit.
    private static bool MarkLiteralSpan(ReadOnlySpan<char> haystack, ReadOnlySpan<char> needle, StringComparison comparison, Span<bool> highlights, out int shape)
    {
        shape = MatchRank.ShapeNoName;
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
            shape = Math.Min(shape, MatchShape.Classify(haystack, absolute, absolute + needle.Length));

            foundAny = true;
            startIdx = absolute + 1;
        }

        return foundAny;
    }

    // The ranking measure for one candidate's mask: how much of the string the match covers, weighted by
    // how contiguous that coverage is: weight = percentage * consecutiveness. Both factors are <= 1, so
    // this only ever demotes a match relative to its raw score -- it's a ranking multiplier, never a
    // gate; a candidate that already passed the real fzf match always stays a match regardless of this
    // weight. Shape and start position are deliberately NOT folded in here: they are separate,
    // higher-priority keys in every window's ordering (see MatchRank / SearchResultRelevance /
    // RankAndDedupe), so a match beginning earlier wins even against a shorter name whose coverage share
    // is larger.
    private static MatchRank RankFromMarks(ReadOnlySpan<bool> mask)
    {
        if (mask.Length == 0)
            return MatchRank.NoMatch;

        var matchedLength = 0;
        var leftmost = -1;
        var sumOfSquares = 0L;
        var runLength = 0;
        for (var i = 0; i < mask.Length; i++)
        {
            if (!mask[i])
            {
                if (runLength > 0)
                {
                    sumOfSquares += (long)runLength * runLength;
                    runLength = 0;
                }
                continue;
            }

            if (leftmost < 0)
                leftmost = i;
            matchedLength++;
            runLength++;
        }
        if (runLength > 0)
            sumOfSquares += (long)runLength * runLength;

        if (matchedLength == 0)
            return MatchRank.NoMatch;

        var percentage = (double)matchedLength / mask.Length;
        var consecutiveness = (double)sumOfSquares / ((long)matchedLength * matchedLength);
        // Tier is filled in by ComputeRank, which knows which path produced the mask; the mask alone
        // cannot say how any term matched.
        return new MatchRank(MatchRank.TierFull, leftmost, percentage * consecutiveness);
    }
}
