using Lertaro.Core.SearchIndex.Fzf;

namespace Lertaro.Core.SearchIndex;

// Which positions of an alias a match is PAINTED on. Split out of AliasHighlightMarker (composition, not a
// partial class) to keep that file under the repository's per-file line limit; everything here answers the
// same one question, and matching never asks it -- matching decides WHETHER an alias matches, this decides
// what the user sees when it does.
//
// Painting has to agree with matching, and matching reads a transliteration both ways (see
// FzfPatternAliasReading), so painting offers the same two readings in the same order: the contiguous run
// first, then the subsequence. Two things keep that honest rather than merely permissive:
//
//   * the alignment rule: a precise query may only be painted where matching would have accepted it, i.e. on
//     a syllable boundary, which is why a loose occurrence that starts mid-syllable is looked past;
//   * the same tightness bar the alias gate applies (AliasMatchRules.IsTightOccurrence), because the gate
//     accepts a match when each term has a TIGHT occurrence somewhere -- painting the greedy leftmost
//     alignment instead lit characters the gate had not accepted (a 7-letter query lit six characters over
//     116 positions of a flattened initials alias).
//
// A provider that declares no separator gets only the reading its term kind implies: with no boundary to
// align to, a loose reading here could paint a splice that matching refused, and an empty mask is not
// "no match" to every caller.
internal static class AliasHighlightPositions
{
    /// <summary>Positions to paint for a term matched against one alias STRING.</summary>
    public static int[]? ForProbe(string aliasLower, string probe, char separator, FzfTermKind kind, bool precise)
    {
        var own = kind == FzfTermKind.Fuzzy
            ? Tightest(aliasLower, probe, separator, precise)
            : Contiguous(aliasLower, probe, separator);
        if (own != null || separator == '\0' || kind is not (FzfTermKind.Fuzzy or FzfTermKind.Exact))
            return own;

        // A precise term whose run the alias does not contain is exactly the abbreviation the loose reading
        // explains ("gxiao" = 高's initial + 效's whole reading), which is what matching accepts too.
        // Contiguous for every other kind, because a scattered subsequence lights up characters that had
        // nothing to do with the hit: "gsh" matches 格式化 through the initials alias, but a subsequence
        // search also finds g...s...h across the full pinyin and lit 创 along with it.
        return kind == FzfTermKind.Fuzzy
            ? Contiguous(aliasLower, probe, separator)
            : Tightest(aliasLower, probe, separator, precise);
    }

    /// <summary>Positions to paint for a term the reading GRID witnessed, whose alias string was materialized.</summary>
    public static int[]? ForGridProbe(string aliasLower, string probe, char separator, FzfTermKind kind, bool precise)
    {
        var own = kind == FzfTermKind.Fuzzy
            ? GreedyOrAligned(aliasLower, probe, separator, precise)
            : Contiguous(aliasLower, probe, separator);
        if (own != null || separator == '\0' || kind is not (FzfTermKind.Fuzzy or FzfTermKind.Exact))
            return own;

        return kind == FzfTermKind.Fuzzy
            ? Contiguous(aliasLower, probe, separator)
            : GreedyOrAligned(aliasLower, probe, separator, precise);
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

    // The grid tier's subsequence finder stays greedy and takes no tightness bar: that tier materialized the
    // witness the matcher had already accepted, so a stricter filter here could paint nothing on a row the
    // grid had matched -- and an empty mask is not "no match" to every caller.
    private static int[]? GreedyOrAligned(string aliasLower, string probe, char separator, bool precise)
    {
        var positions = GreedySubsequence(aliasLower, probe);
        if (!precise || (positions != null && AliasMatchRules.IsBoundaryAligned(separator, aliasLower, positions[0])))
            return positions;

        return TightestAlignedOccurrence(aliasLower, probe, separator);
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

    // Finds ANY valid subsequence alignment of `term` within `text`, returning the matched positions in
    // `text` in order, or null if no such subsequence exists. Greedy (always takes the earliest possible
    // next position), which is enough for a highlight/weight mask -- this doesn't need the optimal/
    // highest-scoring alignment, just a real one.
    private static int[]? GreedySubsequence(string text, string term)
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
