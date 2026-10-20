using System.Globalization;

namespace Lertaro.Core.SearchIndex;

// True when a word starts (or ends) between text[index - 1] and text[index]; the two ends of the text
// are always boundaries. Used by the pinyin position bonus and by MatchShape's classification.
internal static class TextBoundary
{
    internal static bool IsBoundary(ReadOnlySpan<char> text, int index)
        => IsBoundary(text, index, uncasedIsBoundary: true);

    // uncasedIsBoundary=false is for shape classification: an unspaced run of CJK/kana is one word, so
    // "屏幕" is a prefix (word start) and "截图" inside "屏幕截图" is inside the word. The pinyin position
    // bonus keeps the historical true reading.
    internal static bool IsBoundary(ReadOnlySpan<char> text, int index, bool uncasedIsBoundary)
    {
        if (index <= 0 || index >= text.Length)
            return true;

        var previous = KindOf(text[index - 1]);
        var current = KindOf(text[index]);
        if (previous == Kind.Separator || current == Kind.Separator)
            return previous != current;
        if (previous == Kind.Uncased || current == Kind.Uncased)
            return previous == Kind.Uncased && current == Kind.Uncased ? uncasedIsBoundary : true;
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
