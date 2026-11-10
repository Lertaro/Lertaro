using System.Text;
using Lertaro.Core.SearchIndex.Fzf;

namespace Lertaro.Core.Tests.SearchIndex.Fzf;

[TestClass]
public sealed class FzfSingleCharacterFastPathTests
{
    private static readonly string[] Texts =
    [
        "a", "A", "apex", "Apex.exe", "x-a", "x_a", "x a", "xa", "ax", "a---a",
        "foo/a/bar", "fooAbar", "123a", "a123", ".a", "a.", "中a文", "éa", "a中", "zzz",
        "", "K", "Kk", "Kxxk", "kxxK", "İi", "ıI", "éÉ", "Σσς",
    ];

    private static readonly char[] Patterns = ['a', 'A', 'z', '1', '.', '/', '中', 'é', 'k', 'i', 'σ'];

    [TestMethod]
    public void CharacterFastPathMatchesTheFullV2SingleCharacterAlgorithm()
    {
        foreach (var text in Texts)
        foreach (var pattern in Patterns)
        foreach (var scheme in Enum.GetValues<FzfScoringScheme>())
        foreach (var caseSensitive in new[] { false, true })
        {
            var query = (caseSensitive ? pattern : char.ToLowerInvariant(pattern)).ToString();
            var expected = FzfFuzzyMatcher.FuzzyMatchV2(
                text, query, caseSensitive, scheme, enableFastPaths: false);
            var actual = FzfFuzzyMatcher.FuzzyMatchV2(
                text, query, caseSensitive, scheme);

            Assert.AreEqual(expected, actual, $"{text} / {pattern} / {scheme} / case={caseSensitive}");
        }
    }

    [TestMethod]
    public void ByteFastPathMatchesTheFullV2SingleCharacterAlgorithm()
    {
        foreach (var text in Texts.Where(text => Ascii.IsValid(text)))
        foreach (var pattern in Patterns.Where(c => c < 128))
        foreach (var scheme in Enum.GetValues<FzfScoringScheme>())
        foreach (var caseSensitive in new[] { false, true })
        {
            var textBytes = Encoding.ASCII.GetBytes(text);
            var query = (byte)(caseSensitive ? pattern : char.ToLowerInvariant(pattern));
            var expected = FzfByteMatcher.FuzzyMatchV2(
                textBytes, [query], caseSensitive, scheme,
                new FzfSlab(), new FzfByteBuffers(), enableFastPaths: false);
            var actual = FzfByteMatcher.FuzzyMatchV2(
                textBytes, [query], caseSensitive, scheme,
                new FzfSlab(), new FzfByteBuffers());

            Assert.AreEqual(expected, actual, $"{text} / {pattern} / {scheme} / case={caseSensitive}");
        }
    }

    [TestMethod]
    public void FastPathStillPrefersTheBoundaryScoreWithoutMovingTheMatch()
    {
        var match = FzfFuzzyMatcher.FuzzyMatchV2(
            "x---a-more-a", "a", caseSensitive: false, FzfScoringScheme.Default);

        Assert.AreEqual(4, match.Start);
        Assert.AreEqual(5, match.End);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void SingleCharacter_ReturnsTheKnownBoundaryScore(bool enableFastPaths)
    {
        var expected = new FzfMatchResult(0, 1, 36);
        Assert.AreEqual(expected, FzfFuzzyMatcher.FuzzyMatchV2("a", "a", false,
            FzfScoringScheme.Default, enableFastPaths: enableFastPaths));
        Assert.AreEqual(expected, FzfByteMatcher.FuzzyMatchV2("a"u8, [(byte)'a'], false,
            FzfScoringScheme.Default, new FzfSlab(), new FzfByteBuffers(), enableFastPaths));
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void LongTextWithShortScope_PreservesSingleCharacterScore(bool enableFastPaths)
    {
        var offset = FzfAlgorithm.MaxV2Cells + 1;
        var text = new string('x', offset) + "a";
        var expected = new FzfMatchResult(offset, offset + 1, FzfAlgorithm.ScoreMatch);
        Assert.AreEqual(expected, FzfFuzzyMatcher.FuzzyMatchV2(text, "a", false,
            FzfScoringScheme.Default, enableFastPaths: enableFastPaths));
        Assert.AreEqual(expected, FzfByteMatcher.FuzzyMatchV2(Encoding.ASCII.GetBytes(text), [(byte)'a'], false,
            FzfScoringScheme.Default, new FzfSlab(), new FzfByteBuffers(), enableFastPaths));
    }

    [TestMethod]
    public void OversizedScope_StillUsesTheOriginalV1Fallback()
    {
        var text = "xa" + new string('x', FzfAlgorithm.MaxV2Cells) + " a";
        var expected = new FzfMatchResult(text.Length - 1, text.Length, 36);
        var v1 = FzfFuzzyMatcher.FuzzyMatchV1(text, "a", false, FzfScoringScheme.Default);
        Assert.AreEqual(expected, v1);
        foreach (var enabled in new[] { true, false })
        {
            Assert.AreEqual(expected, FzfFuzzyMatcher.FuzzyMatchV2(text, "a", false,
                FzfScoringScheme.Default, enableFastPaths: enabled));
            Assert.AreEqual(expected, FzfByteMatcher.FuzzyMatchV2(Encoding.ASCII.GetBytes(text), [(byte)'a'], false,
                FzfScoringScheme.Default, new FzfSlab(), new FzfByteBuffers(), enabled));
        }
    }
}
