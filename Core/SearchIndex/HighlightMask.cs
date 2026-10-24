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
    [ThreadStatic]
    private static char[]? _threadNormalized;

    private static FzfSlab RentSlab() => _threadSlab ??= new FzfSlab();

    public static bool[] Compute(string fullText, FzfPattern pattern)
    {
        var highlights = new bool[fullText.Length];
        if (fullText.Length == 0)
            return highlights;

        var text = Normalize(fullText, out var map);
        var marks = text.Length <= 512 ? stackalloc bool[text.Length] : new bool[text.Length];
        marks.Clear();
        string? materialized = null;
        Mark(text, pattern, marks, ref materialized, RentSlab(), pinyinWitness: null, out _, out _);

        for (var i = 0; i < marks.Length; i++)
        {
            if (!marks[i])
                continue;

            if (map == null)
            {
                highlights[i] = true;
                continue;
            }

            var originalStart = map[i];
            // Several normalized units can belong to one source grapheme (NFC composition); paint the
            // whole source cluster, not a zero-width range when the next unit maps to the same index.
            var originalEnd = fullText.Length;
            for (var k = i + 1; k < map.Length; k++)
            {
                if (map[k] > originalStart)
                {
                    originalEnd = map[k];
                    break;
                }
            }
            for (var k = originalStart; k < originalEnd && k < highlights.Length; k++)
                highlights[k] = true;
        }

        return highlights;
    }

    // Ranking-facing: same computation, but works directly off a char span -- the (common) literal and
    // direct-fuzzy tiers never materialize a string at all; a string is only built if some term needs
    // the alias-provider tier, which requires the AliasProviderRegistry/IAliasProvider string APIs.
    public static double ComputeWeight(ReadOnlySpan<char> fullText, FzfPattern pattern)
        => ComputeRank(fullText, pattern).Weight;

    // Match tier, shape, start position, coverage and contiguity from ONE mask pass. Every ranking
    // window needs all of them (see MatchRank), and computing them separately would build the same mask
    // several times, the alias path being the expensive one.
    public static MatchRank ComputeRank(ReadOnlySpan<char> fullText, FzfPattern pattern) =>
        ComputeRank(fullText, pattern, out _);

    internal static MatchRank ComputeRank(
        ReadOnlySpan<char> fullText,
        FzfPattern pattern,
        out AcceptedMatchWitness witness) =>
        ComputeRank(fullText, pattern, null, out witness);

    internal static MatchRank ComputeRank(
        ReadOnlySpan<char> fullText,
        FzfPattern pattern,
        IndexV2.Search.PinyinNameEvaluator.PinyinMatchWitness? pinyinWitness,
        out AcceptedMatchWitness witness)
    {
        witness = AcceptedMatchWitness.LegacyUnknown;
        if (fullText.Length == 0)
            return MatchRank.NoMatch;

        var text = Normalize(fullText, out var map);
        var marks = text.Length <= 512 ? stackalloc bool[text.Length] : new bool[text.Length];
        marks.Clear();
        string? materialized = null;
        var tier = Mark(
            text,
            pattern,
            marks,
            ref materialized,
            RentSlab(),
            pinyinWitness,
            out var shape,
            out witness);
        var rank = RankFromMarks(marks, CountPositiveTerms(pattern));
        if (!rank.IsMatch)
            return rank;

        if (map != null)
        {
            if (rank.Start < map.Length)
                rank = rank with { Start = map[rank.Start] };
            if (witness.SpanStart >= 0 && witness.SpanStart < map.Length)
            {
                var normalizedEnd = Math.Min(map.Length, witness.SpanStart + witness.SpanLength);
                var originalStart = map[witness.SpanStart];
                var originalEnd = normalizedEnd < map.Length ? map[normalizedEnd] : fullText.Length;
                witness = witness with
                {
                    SpanStart = originalStart,
                    SpanLength = Math.Max(0, originalEnd - originalStart),
                };
            }
        }

        // The mask is a union across terms, so it cannot say HOW any one of them matched; Mark reports the
        // strongest tier and the folded shape, which is what decides the ordering.
        return rank with { Tier = tier, Shape = shape };
    }

    private static ReadOnlySpan<char> Normalize(ReadOnlySpan<char> text, out int[]? map)
    {
        var length = SearchTextNormalizer.NormalizeInto(text, ref _threadNormalized, out map);
        return length == 0 ? text : _threadNormalized.AsSpan(0, length);
    }

    // Paints every matching term and returns the strongest tier reached and the folded shape.
    // Tier takes the minimum; shape folds the other way round, following the query's own logic: within a
    // set (an OR group) the best-shaped term counts, and across sets (AND) the worst set decides -- "code
    // atlas" against "code-atlasing" is only as good as its weaker word. A set with nothing marked (the
    // match came through a route the mask cannot paint) is treated as scattered rather than guessed better.
    private static int Mark(
        ReadOnlySpan<char> fullText,
        FzfPattern pattern,
        Span<bool> highlights,
        ref string? materialized,
        FzfSlab slab,
        IndexV2.Search.PinyinNameEvaluator.PinyinMatchWitness? pinyinWitness,
        out int shape,
        out AcceptedMatchWitness witness)
    {
        witness = AcceptedMatchWitness.LegacyUnknown;
        var joined = pinyinWitness ?? default;
        if (pinyinWitness is null)
        {
            // No accepted witness from the caller: ask the evaluator which branch won. A caller that already
            // holds the matcher's answer must not ask twice -- the second TryScore scored the same text a
            // second time for every multi-term row whose joined branch lost.
            var live = IndexV2.Search.PinyinNameEvaluator.ForPattern(pattern);
            if (!live.HasJoinedBranch || !live.TryScore(fullText, null, out _, out joined) || !joined.IsJoined)
                joined = default;
        }
        if (joined.IsJoined)
        {
            for (var i = joined.Start; i < joined.End && i < highlights.Length; i++)
                highlights[i] = true;
            shape = MatchRank.ShapeWordStart;
            witness = new AcceptedMatchWitness(
                PinyinMode(joined.Tier),
                MatchQueryVariant.PinyinJoined,
                joined.ProviderName,
                joined.Start,
                joined.End - joined.Start);
            return joined.Tier;
        }

        var tier = MatchRank.TierFullRare;
        shape = MatchRank.ShapeWholeWord;
        foreach (var set in pattern.EffectiveSets)
        {
            var setShape = MatchRank.ShapeNoName;
            var hasPositiveTerm = false;
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

    // Returns the strongest tier this term matched through and how it sat in the text. TierName is the
    // candidate's own text; WordStart is the only defensible shape for a provider-supplied spelling,
    // because that spelling was matched at a syllable boundary; Scattered is a fuzzy subsequence.
    private static int MarkTerm(ReadOnlySpan<char> fullText, string term, bool caseSensitive, FzfTermKind kind, Span<bool> highlights, ref string? materialized, FzfSlab slab, FzfPattern pattern, out int shape)
    {
        var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        if (MarkLiteralSpan(fullText, term, comparison, highlights, out shape))
            return MatchRank.TierName;

        // The live reading tier: the reading NFA decides the term from the name's own readings and
        // paints the span it consumed -- exactly the position set the searcher's match used. It sits
        // where the reading grid used to: before the string aliases, because a provider's own
        // spellings are the meaningful explanation whenever they exist, while the name's scattered
        // pass below is the fallback for a query no provider can express.
        var live = IndexV2.Search.PinyinNameEvaluator.ForPattern(pattern);
        if (live.HasProviders && live.TryPaintTerm(fullText, term, kind, highlights, out var liveTier))
        {
            shape = MatchRank.ShapeWordStart;
            return liveTier;
        }

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
        {
            AliasHighlightMarker.MarkViaMixedQuery(materialized, mixedTerm, highlights);
            shape = MatchRank.ShapeWordStart;
        }
        else
        {
            shape = MatchRank.ShapeNoName;
        }

        return MatchRank.TierFull;
    }

    // Paints every occurrence and reports the best shape among them.
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

    private static MatchEvidenceMode PinyinMode(int tier) => tier switch
    {
        MatchRank.TierInitials => MatchEvidenceMode.PinyinInitials,
        MatchRank.TierFullRare => MatchEvidenceMode.PinyinRare,
        _ => MatchEvidenceMode.PinyinFull,
    };

    // The ranking measure for one candidate's mask: how much of the string the match covers, weighted by
    // how contiguous that coverage is (coverage * contiguity). Shape is filled in by ComputeRank, which
    // knows which path produced the mask; the mask alone cannot classify the term.
    //
    // A multi-term query is allowed to have one run per term: "cf wj" against "cf 桌面文件" is two clean
    // terms, not one broken span. Counting runs (instead of the single-run sum of squares) gives such a
    // match contiguity 1, so Coverage decides between it and a single-term hit. A term that is itself
    // scattered still adds extra runs and keeps contiguity below 1.
    //
    // ponytail: runs are counted on the union mask, so an OR set whose alternatives both appear adds
    // extra runs for one typed term. Per-term masks would be exact but would double the matcher work on
    // the hot path; revisit if an OR-heavy query starts mis-ranking.
    private static MatchRank RankFromMarks(ReadOnlySpan<bool> mask, int positiveTermCount)
    {
        if (mask.Length == 0)
            return MatchRank.NoMatch;

        var matchedLength = 0;
        var leftmost = -1;
        var runCount = 0;
        var sumOfSquares = 0L;
        var runLength = 0;
        for (var i = 0; i < mask.Length; i++)
        {
            if (!mask[i])
            {
                if (runLength > 0)
                {
                    runCount++;
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
        {
            runCount++;
            sumOfSquares += (long)runLength * runLength;
        }

        if (matchedLength == 0)
            return MatchRank.NoMatch;

        var coverage = (double)matchedLength / mask.Length;
        var contiguity = positiveTermCount > 1
            ? Math.Min(1d, (double)positiveTermCount / runCount)
            : (double)sumOfSquares / ((long)matchedLength * matchedLength);
        return new MatchRank(MatchRank.TierFull, leftmost, coverage, contiguity);
    }

    private static int CountPositiveTerms(FzfPattern pattern)
    {
        var count = 0;
        foreach (var set in pattern.EffectiveSets)
        {
            foreach (var term in set.Terms)
            {
                if (!term.Inverse)
                {
                    count++;
                    break;
                }
            }
        }

        return count;
    }
}
