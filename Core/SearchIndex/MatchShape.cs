namespace Lertaro.Core.SearchIndex;

// Classifies one contiguous occurrence of a term inside a text as whole word / word start / inside word.
// A word starts at the beginning of the text, after a separator (space, path delimiter, '-', '_', '.', ...),
// at a lower->Upper camelCase step, and where letters meet digits -- the same boundaries fzf's bonus table
// rewards, so "atlas" is a whole word in "code-atlas", "atlas.png", "CodeAtlas", "AtlasViewer" and
// "atlas01", a word start in "atlasing", and inside a word in "metatlas".
//
// ponytail: an unspaced script (CJK, kana) is treated as one word in shape classification: "屏幕" in
// "屏幕截图" is a word start, "截图" inside it is inside. This is position-based, not a real segmenter --
// upgrading means a per-script word segmenter, not another heuristic.
internal static class MatchShape
{
    public static int Classify(ReadOnlySpan<char> text, int start, int end)
    {
        if (!TextBoundary.IsBoundary(text, start, uncasedIsBoundary: false))
            return MatchRank.ShapeInsideWord;
        return TextBoundary.IsBoundary(text, end, uncasedIsBoundary: false)
            ? MatchRank.ShapeWholeWord
            : MatchRank.ShapeWordStart;
    }
}
