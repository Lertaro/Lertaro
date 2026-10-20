using Lertaro.Core.SearchIndex;
using Lertaro.Core.SearchIndex.Fzf;
using Lertaro.Core.SearchIndex.Query;

namespace Lertaro.Core.Tests.SearchIndex;

// Full-width IME output, ideographic spaces and NFD names all represent the same text a user sees.
// Matching folds them; display/highlighting must still address the original UTF-16 positions.
[TestClass]
public sealed class SearchTextNormalizerTests
{
    [TestMethod]
    public void Normalize_FullWidthAsciiAndIdeographicSpace()
    {
        Assert.AreEqual("FSCapture2024 ", SearchTextNormalizer.Normalize("ＦＳＣａｐｔｕｒｅ２０２４　"));
    }

    [TestMethod]
    public void NeedsNormalization_OnlyWhenFoldingCanChangeTheText()
    {
        Assert.IsFalse(SearchTextNormalizer.NeedsNormalization("FastStone Capture"));
        Assert.IsTrue(SearchTextNormalizer.NeedsNormalization("ｆａｓｔ"));
        Assert.IsTrue(SearchTextNormalizer.NeedsNormalization("fast　stone"));
        Assert.IsTrue(SearchTextNormalizer.NeedsNormalization("e\u0301"));
    }

    [TestMethod]
    public void FullWidthQuery_MatchesHalfWidthName()
    {
        Assert.IsTrue(FuzzyMatcher.IsMatch("ｆａｓｔ", "fast"));
    }

    [TestMethod]
    public void HalfWidthQuery_MatchesFullWidthName()
    {
        Assert.IsTrue(FuzzyMatcher.IsMatch("fast", "ｆａｓｔ"));
    }

    [TestMethod]
    public void IdeographicSpace_IsATermSeparator()
    {
        Assert.IsTrue(FuzzyMatcher.IsMatch("fast　stone", "FastStone Capture"));
    }

    [TestMethod]
    public void TabAndNewline_AreTermSeparators()
    {
        Assert.IsTrue(FuzzyMatcher.IsMatch("fast\tstone", "FastStone Capture"));
        Assert.IsTrue(FuzzyMatcher.IsMatch("fast\nstone", "FastStone Capture"));
    }

    [TestMethod]
    public void FullWidthName_HighlightMaskStaysOnOriginalPositions()
    {
        var mask = FuzzyMatcher.ComputeHighlightMask("ｆａｓｔ", "fast");

        Assert.HasCount(4, mask);
        Assert.IsTrue(Array.TrueForAll(mask, m => m));
        var rank = FuzzyMatcher.ComputeMatchRank("ｆａｓｔ", "fast");
        Assert.AreEqual(0, rank.Start);
        Assert.AreEqual(1.0, rank.Coverage);
    }

    [TestMethod]
    public void NfdName_MatchesComposedQuery()
    {
        const string name = "cafe\u0301.txt";
        Assert.IsTrue(FuzzyMatcher.IsMatch("café", name));

        var mask = FuzzyMatcher.ComputeHighlightMask(name, "café");
        // c a f e + the combining mark are all part of the displayed "café" cluster.
        Assert.IsTrue(mask[0] && mask[1] && mask[2] && mask[3] && mask[4]);
    }

    [TestMethod]
    public void FullWidthDrivePath_IsFoldedBeforePathModeParsing()
    {
        var parsed = SearchQueryParser.Parse("ｃ：\\ｆａｓｔ");

        Assert.IsTrue(parsed.IsPathMode);
        Assert.AreEqual("c", parsed.TargetDrive);
        Assert.AreEqual(@"c:\fast", parsed.PathPatternLower);
    }

    [TestMethod]
    public void InverseOnlyQuery_ProducesARankRatherThanNoMatch()
    {
        var rank = FuzzyMatcher.ComputeMatchRank("abc.txt", "!zzz");

        Assert.IsTrue(rank.IsMatch);
        Assert.IsFalse(rank.IsLeftContiguous);
    }

    [TestMethod]
    public void ExcludedName_IsNotARankMatch()
    {
        var rank = FuzzyMatcher.ComputeMatchRank("code-atlas.txt", "atlas !code");

        Assert.IsFalse(rank.IsMatch);
    }
}
