using System.Globalization;

namespace Lertaro.Core.SearchIndex;

// Classifies one contiguous occurrence of a term inside a text as MatchRank.ShapeWholeWord /
// ShapeWordStart / ShapeInsideWord. A word starts at the beginning of the text, after a separator
// (space, path delimiter, '-', '_', '.', ...), at a lower->Upper camelCase step, and where letters meet
// digits -- the same boundaries fzf's bonus table rewards, so "atlas" is a whole word in "code-atlas",
// "atlas.png", "CodeAtlas", "AtlasViewer" and "atlas01", and only a word start in "atlasing".
//
// ponytail: an uncased letter (CJK, kana, but also Arabic/Hebrew/Thai) counts as a word on its own,
// because ideographic names have no separators to find words by. Ceiling: in uncased scripts that DO
// form multi-letter words, every contiguous hit ranks as a whole word, i.e. shape stops discriminating
// there and ordering falls back to start/weight as before. Upgrade: a real word segmenter per script.
internal static class MatchShape
{
    public static int Classify(ReadOnlySpan<char> text, int start, int end)
    {
        if (!IsBoundary(text, start))
            return MatchRank.ShapeInsideWord;
        return IsBoundary(text, end) ? MatchRank.ShapeWholeWord : MatchRank.ShapeWordStart;
    }

    // True when a word starts (or ends) between text[index - 1] and text[index]; the two ends of the text
    // are always boundaries.
    internal static bool IsBoundary(ReadOnlySpan<char> text, int index)
    {
        if (index <= 0 || index >= text.Length)
            return true;

        var previous = KindOf(text[index - 1]);
        var current = KindOf(text[index]);
        if (previous == Kind.Separator || current == Kind.Separator)
            return previous != current;
        if (previous == Kind.Uncased || current == Kind.Uncased)
            return true;
        if ((previous == Kind.Digit) != (current == Kind.Digit))
            return true;
        return previous == Kind.Lower && current == Kind.Upper;
    }

    private enum Kind { Separator, Lower, Upper, Digit, Uncased }

    private static Kind KindOf(char c)
    {
        if (char.IsDigit(c))
            return Kind.Digit;
        if (char.IsLower(c))
            return Kind.Lower;
        if (char.IsUpper(c) || CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.TitlecaseLetter)
            return Kind.Upper;
        return char.IsLetter(c) ? Kind.Uncased : Kind.Separator;
    }
}
