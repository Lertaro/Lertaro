using Lertaro.Core.SearchIndex.Fzf;

namespace Lertaro.Core.SearchIndex;

// Finds alias highlight positions using the same reading order as matching: contiguous first, then
// subsequence. Require syllable alignment and AliasMatchRules.IsTightOccurrence so highlights reflect an
// accepted occurrence. Providers without separators keep the term's original matching mode.
internal static class AliasHighlightPositions
{
    /// <summary>Positions to paint for a term matched against one alias STRING.</summary>
    public static int[]? ForProbe(string aliasLower, string probe, char separator, FzfTermKind kind, bool precise)
    {
        // An operator's run is the only thing it means.
        if (kind is not (FzfTermKind.Fuzzy or FzfTermKind.Exact))
            return Contiguous(aliasLower, probe, separator);

        // A provider with no declared structure has nothing to align to, and an alias that rewrites the
        // candidate's own script spells nothing out, so either gets the one reading its term kind implies.
        if (separator == '\0' || !AliasMatchRules.IsTransliteration(aliasLower))
        {
            return kind == FzfTermKind.Fuzzy
                ? Tightest(aliasLower, probe, separator, precise)
                : Contiguous(aliasLower, probe, separator);
        }

        // A provider with structure is painted the way matching reads it (FzfPatternAliasReading): a real
        // run first, the loose occurrence only when no run explains the term. Keyed on the structure rather
        // than on the term's kind, because pinyin is read the same in both matching modes -- the same row
        // has to paint the same characters with fuzzy matching on and off.
        //
        // The rule is what the deleted reading-grid tier taught: its materialized witness was an alias
        // string like any other, and the loose greedy scatter it used to be painted with lit characters
        // belonging to neither the term nor the spelling that matched it (a 184-character name's "fangdan"
        // lit four unrelated positions elsewhere in the name).
        return Contiguous(aliasLower, probe, separator)
               ?? Tightest(aliasLower, probe, separator, precise);
    }

    // The term's own tightest occurrence, held to the boundary rule a precise query's matches are held to.
    private static int[]? Tightest(string aliasLower, string probe, char separator, bool precise)
    {
        var occurrence = AliasMatchRules.TightestOccurrence(aliasLower, probe, caseSensitive: false);
        if (occurrence != null && precise && !AliasMatchRules.IsBoundaryAligned(separator, aliasLower, occurrence[0]))
            occurrence = TightestAlignedOccurrence(aliasLower, probe, separator);

        if (occurrence == null || !AliasMatchRules.IsTightOccurrence(probe.Length, occurrence[^1] - occurrence[0] + 1))
            return null;
        return occurrence;
    }

    /// <summary>
    /// The narrowest occurrence of <paramref name="term"/> in <paramref name="text"/> that BEGINS on a
    /// syllable boundary, or null when no such occurrence exists.
    /// </summary>
    /// <remarks>
    /// Only ever needed where the unrestricted tightest occurrence is misaligned: "gq", the tail of ting glued
    /// to the head of qin, is that shape, and the occurrence matching would have accepted -- the scored
    /// alignment, whose start the boundary rule checks -- is what this finds.
    ///
    /// A separate walk rather than a filter over <see cref="AliasMatchRules.TightestOccurrence"/>'s result:
    /// the backward pull that narrows a window can land before the boundary, and dropping that one window
    /// would also drop the aligned window ending in the same place.
    /// </remarks>
    private static int[]? TightestAlignedOccurrence(ReadOnlySpan<char> text, ReadOnlySpan<char> term, char separator)
    {
        if (term.IsEmpty || text.Length < term.Length)
            return null;

        int[]? best = null;
        for (var start = 0; start + term.Length <= text.Length; start++)
        {
            // The match has to BEGIN at `start`, so the term's own first character has to be there: a
            // boundary-aligned scan origin whose first matched character sits later is not an aligned match,
            // and reporting it as one would paint the splice this exists to refuse.
            if (!CharsEqual(text[start], term[0]))
                continue;
            if (!AliasMatchRules.IsBoundaryAligned(separator, text, start))
                continue;

            var ti = 0;
            var j = start;
            while (j < text.Length && ti < term.Length)
            {
                if (CharsEqual(text[j], term[ti]))
                    ti++;
                j++;
            }
            if (ti < term.Length)
                continue;

            if (best != null && j - start >= best[^1] - best[0] + 1)
                continue;

            var positions = new int[term.Length];
            var pos = 0;
            ti = 0;
            for (var i = start; i < j && ti < term.Length; i++)
            {
                if (CharsEqual(text[i], term[ti]))
                {
                    positions[pos++] = i;
                    ti++;
                }
            }
            best = positions;
        }

        return best;
    }

    private static bool CharsEqual(char left, char right)
        => char.ToLowerInvariant(left) == char.ToLowerInvariant(right);

    // Contiguous counterpart of the walk above, for when matching itself demands a contiguous run.
    //
    // Occurrences are scanned until one starts on a syllable boundary (see AliasMatchRules.IsBoundaryAligned).
    // Skipping a misaligned occurrence rather than giving up is deliberate: this side must never be
    // STRICTER than matching, or a row that matched would render with nothing highlighted. Matching
    // rejects only when the first occurrence it scored is misaligned, so accepting any aligned occurrence
    // here keeps the two in agreement for every realistic alias -- and errs toward lighting something up.
    private static int[]? Contiguous(string text, string term, char separator)
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
