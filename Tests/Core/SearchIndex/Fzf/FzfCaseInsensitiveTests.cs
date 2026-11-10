using System.Text;
using Lertaro.Core.SearchIndex.Fzf;

namespace Lertaro.Core.Tests.SearchIndex.Fzf;

[TestClass]
public sealed class FzfCaseInsensitiveTests
{
    [TestMethod]
    public void CharacterMatcherNormalizesBothSidesWhenCaseInsensitive()
    {
        foreach (var scheme in Enum.GetValues<FzfScoringScheme>())
        {
            var lower = FzfFuzzyMatcher.FuzzyMatchV2(
                "application", "appl", caseSensitive: false, scheme, enableFastPaths: false);
            var mixed = FzfFuzzyMatcher.FuzzyMatchV2(
                "application", "Appl", caseSensitive: false, scheme, enableFastPaths: false);

            Assert.IsTrue(lower.IsMatch);
            Assert.AreEqual(lower, mixed, scheme.ToString());
        }
    }

    [TestMethod]
    public void ByteMatcherNormalizesBothSidesWhenCaseInsensitive()
    {
        var text = Encoding.ASCII.GetBytes("application");
        foreach (var scheme in Enum.GetValues<FzfScoringScheme>())
        {
            var lower = FzfByteMatcher.FuzzyMatchV2(
                text, Encoding.ASCII.GetBytes("appl"), caseSensitive: false, scheme,
                new FzfSlab(), new FzfByteBuffers(), enableFastPaths: false);
            var mixed = FzfByteMatcher.FuzzyMatchV2(
                text, Encoding.ASCII.GetBytes("Appl"), caseSensitive: false, scheme,
                new FzfSlab(), new FzfByteBuffers(), enableFastPaths: false);

            Assert.IsTrue(lower.IsMatch);
            Assert.AreEqual(lower, mixed, scheme.ToString());
        }
    }

    [TestMethod]
    public void SingleCharacterFastPathsNormalizePatternCase()
    {
        var lower = FzfFuzzyMatcher.FuzzyMatchV2(
            "apex", "a", caseSensitive: false, FzfScoringScheme.Default);
        var upper = FzfFuzzyMatcher.FuzzyMatchV2(
            "apex", "A", caseSensitive: false, FzfScoringScheme.Default);
        Assert.AreEqual(lower, upper);

        var lowerByte = FzfByteMatcher.FuzzyMatchV2(
            "apex"u8, [(byte)'a'], caseSensitive: false, FzfScoringScheme.Default,
            new FzfSlab(), new FzfByteBuffers());
        var upperByte = FzfByteMatcher.FuzzyMatchV2(
            "apex"u8, [(byte)'A'], caseSensitive: false, FzfScoringScheme.Default,
            new FzfSlab(), new FzfByteBuffers());
        Assert.AreEqual(lowerByte, upperByte);
    }
}
