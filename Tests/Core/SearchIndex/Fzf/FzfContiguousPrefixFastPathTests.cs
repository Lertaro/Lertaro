using System.Text;
using Lertaro.Core.SearchIndex.Fzf;

namespace Lertaro.Core.Tests.SearchIndex.Fzf;

[TestClass]
public sealed class FzfContiguousPrefixFastPathTests
{
    private static readonly string[] Texts =
    [
        "apex", "Apex.exe", "app-report", "application", "a---pe", "ape ape",
        "prefix", "prelude", "not-a-prefix", "中文字", "éclair",
    ];

    private static readonly string[] Patterns = ["ap", "ape", "app", "Appl", "pre", "中", "éc"];

    [TestMethod]
    public void CharacterPrefixFastPathMatchesTheFullV2Algorithm()
    {
        foreach (var text in Texts)
        foreach (var pattern in Patterns)
        foreach (var scheme in Enum.GetValues<FzfScoringScheme>())
        foreach (var caseSensitive in new[] { false, true })
        {
            var expected = FzfFuzzyMatcher.FuzzyMatchV2(
                text, pattern, caseSensitive, scheme, enableFastPaths: false);
            var actual = FzfFuzzyMatcher.FuzzyMatchV2(
                text, pattern, caseSensitive, scheme);

            Assert.AreEqual(expected, actual, $"{text} / {pattern} / {scheme} / case={caseSensitive}");
        }
    }

    [TestMethod]
    public void BytePrefixFastPathMatchesTheFullV2Algorithm()
    {
        foreach (var text in Texts.Where(text => Ascii.IsValid(text)))
        foreach (var pattern in Patterns.Where(text => Ascii.IsValid(text)))
        foreach (var scheme in Enum.GetValues<FzfScoringScheme>())
        foreach (var caseSensitive in new[] { false, true })
        {
            var textBytes = Encoding.ASCII.GetBytes(text);
            var patternBytes = Encoding.ASCII.GetBytes(pattern);
            var expected = FzfByteMatcher.FuzzyMatchV2(
                textBytes, patternBytes, caseSensitive, scheme,
                new FzfSlab(), new FzfByteBuffers(), enableFastPaths: false);
            var actual = FzfByteMatcher.FuzzyMatchV2(
                textBytes, patternBytes, caseSensitive, scheme,
                new FzfSlab(), new FzfByteBuffers());

            Assert.AreEqual(expected, actual, $"{text} / {pattern} / {scheme} / case={caseSensitive}");
        }
    }

    [TestMethod]
    public void PrefixUsesTheExactPrefixScore()
    {
        var match = FzfFuzzyMatcher.FuzzyMatchV2(
            "Apex.exe", "ape", caseSensitive: false, FzfScoringScheme.Default);
        var prefix = FzfExactMatcher.PrefixMatch(
            "Apex.exe", "ape", caseSensitive: false, FzfScoringScheme.Default);

        Assert.AreEqual(prefix, match);
        Assert.AreEqual(0, match.Start);
        Assert.AreEqual(3, match.End);
    }
}
