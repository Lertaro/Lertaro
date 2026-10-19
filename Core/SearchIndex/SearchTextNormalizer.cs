using System.Globalization;
using System.Text;

namespace Lertaro.Core.SearchIndex;

// Search-time text folding. A full-width IME produces U+FF01..U+FF5E for ASCII and U+3000 for a space;
// both must search the same as their half-width forms. Names can also arrive as NFD (a base letter plus
// combining marks), which looks identical to its NFC form but is a different string. Matching therefore
// runs on a folded view while display keeps the original text; a position map carries match positions
// back so highlighting and the left-position rank still point at the characters the user sees.
public static class SearchTextNormalizer
{
    // 0 means "nothing to fold"; callers then use the original span without allocating or mapping.
    public static int NormalizeInto(ReadOnlySpan<char> source, ref char[]? buffer, out int[]? map)
    {
        map = null;
        if (!NeedsNormalization(source))
            return 0;

        var chars = new List<char>(source.Length);
        var originalIndices = new List<int>(source.Length);

        var i = 0;
        while (i < source.Length)
        {
            var length = StringInfo.GetNextTextElementLength(source[i..]);
            if (length <= 0)
                length = 1;

            var element = source.Slice(i, length);
            if (element.Length == 1)
            {
                var c = FoldWidth(element[0]);
                if (c == element[0] && !IsCombining(c))
                {
                    chars.Add(c);
                    originalIndices.Add(i);
                }
                else if (c != element[0] && !IsCombining(c))
                {
                    chars.Add(c);
                    originalIndices.Add(i);
                }
                else
                {
                    AppendNormalized(element, i, chars, originalIndices);
                }
            }
            else
            {
                // A grapheme cluster can compose to a different number of UTF-16 units. Every produced
                // unit points at the cluster's first source unit; the mask mapper paints the whole cluster.
                AppendNormalized(element, i, chars, originalIndices);
            }

            i += length;
        }

        EnsureCapacity(ref buffer, chars.Count);
        for (var k = 0; k < chars.Count; k++)
            buffer![k] = chars[k];
        map = originalIndices.ToArray();
        return chars.Count;
    }

    public static string Normalize(string? text)
    {
        if (string.IsNullOrEmpty(text) || !NeedsNormalization(text))
            return text ?? string.Empty;

        char[]? buffer = null;
        var length = NormalizeInto(text, ref buffer, out _);
        return length == 0 ? text : new string(buffer!, 0, length);
    }

    /// <summary>True when folding can change this text at all. Hot paths use this to skip allocations.</summary>
    public static bool NeedsNormalization(ReadOnlySpan<char> text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\u3000' || (c >= '\uFF01' && c <= '\uFF5E'))
                return true;

            if (char.IsSurrogate(c))
            {
                if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                    i++;
                continue;
            }

            if (IsCombining(c))
                return true;
            // Hangul Jamo compose into syllables under NFC; no single-char category call is needed.
            if (c >= '\u1100' && c <= '\u11FF')
                return true;
        }
        return false;
    }

    private static void AppendNormalized(ReadOnlySpan<char> element, int originalIndex, List<char> chars, List<int> originalIndices)
    {
        var folded = new char[element.Length];
        for (var j = 0; j < element.Length; j++)
            folded[j] = FoldWidth(element[j]);

        var normalized = new string(folded).Normalize(NormalizationForm.FormC);
        foreach (var c in normalized)
        {
            chars.Add(c);
            originalIndices.Add(originalIndex);
        }
    }

    private static char FoldWidth(char c) => c switch
    {
        '\u3000' => ' ',
        >= '\uFF01' and <= '\uFF5E' => (char)(c - 0xFEE0),
        _ => c
    };

    private static bool IsCombining(char c)
    {
        if (char.IsSurrogate(c))
            return false;
        // Common combining-mark blocks first: this runs for every non-ASCII candidate name, and CJK
        // characters must not pay for a CharUnicodeInfo lookup per character.
        if ((c >= '\u0300' && c <= '\u036F') ||
            (c >= '\u1AB0' && c <= '\u1AFF') ||
            (c >= '\u1DC0' && c <= '\u1DFF') ||
            (c >= '\u20D0' && c <= '\u20FF') ||
            (c >= '\uFE20' && c <= '\uFE2F'))
            return true;

        // Letters/digits/punctuation/symbols are never combining marks; skipping the Unicode category
        // lookup keeps the per-candidate scan cheap for CJK names.
        if (char.IsLetterOrDigit(c) || char.IsPunctuation(c) || char.IsSymbol(c))
            return false;

        return CharUnicodeInfo.GetUnicodeCategory(c) is
            UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark;
    }

    private static void EnsureCapacity(ref char[]? buffer, int length)
    {
        if (buffer == null || buffer.Length < length)
            buffer = new char[Math.Max(length, buffer?.Length * 2 ?? 64)];
    }
}
