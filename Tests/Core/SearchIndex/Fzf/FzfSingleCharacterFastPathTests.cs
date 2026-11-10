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
    ];

    private static readonly char[] Patterns = ['a', 'A', 'z', '1', '.', '/', '中', 'é'];

    [TestMethod]
    public void CharacterFastPathMatchesTheFullV2SingleCharacterAlgorithm()
    {
        foreach (var text in Texts)
        foreach (var pattern in Patterns)
        foreach (var scheme in Enum.GetValues<FzfScoringScheme>())
        foreach (var caseSensitive in new[] { false, true })
        {
            var expected = FzfFuzzyMatcher.FuzzyMatchV2(
                text, pattern.ToString(), caseSensitive, scheme, enableFastPaths: false);
            var actual = FzfFuzzyMatcher.FuzzyMatchV2(
                text, pattern.ToString(), caseSensitive, scheme);

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
            var expected = FzfByteMatcher.FuzzyMatchV2(
                textBytes, [(byte)pattern], caseSensitive, scheme,
                new FzfSlab(), new FzfByteBuffers(), enableFastPaths: false);
            var actual = FzfByteMatcher.FuzzyMatchV2(
                textBytes, [(byte)pattern], caseSensitive, scheme,
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
}
