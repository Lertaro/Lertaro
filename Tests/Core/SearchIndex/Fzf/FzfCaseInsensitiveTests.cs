using System.Text;
using Lertaro.Core.SearchIndex.Fzf;

namespace Lertaro.Core.Tests.SearchIndex.Fzf;

[TestClass]
public sealed class FzfCaseInsensitiveTests
{
    [TestMethod]
    [DataRow("application", "ApPl")]
    [DataRow("apricot_pineapple", "APpL")]
    [DataRow("Äpfel", "ÄPF")]
    [DataRow("Αλφα", "ΑΛ")]
    [DataRow("éclair", "É")]
    public void CharacterMatcher_MixedCaseHasTheSameScoreAndSpan(string text, string pattern)
    {
        foreach (var scheme in Enum.GetValues<FzfScoringScheme>())
        {
            var expected = FzfFuzzyMatcher.FuzzyMatchV2(text, pattern.ToLowerInvariant(), false,
                scheme, enableFastPaths: false);
            Assert.IsTrue(expected.IsMatch);
            Assert.AreEqual(expected, FzfFuzzyMatcher.FuzzyMatchV2(text, pattern, false, scheme));
            Assert.AreEqual(expected, FzfFuzzyMatcher.FuzzyMatchV2(text, pattern, false, scheme, enableFastPaths: false));
            Assert.IsFalse(FzfFuzzyMatcher.FuzzyMatchV2(text, pattern, true, scheme).IsMatch);
        }
    }

    [TestMethod]
    public void ByteMatcher_ReusedBuffersAndPatternsKeepCaseSensitiveRequestsIndependent()
    {
        var slab = new FzfSlab();
        var buffers = new FzfByteBuffers();
        var pattern = "APpL"u8.ToArray();
        var expected = FzfByteMatcher.FuzzyMatchV2("apricot_pineapple"u8, "appl"u8.ToArray(), false,
            FzfScoringScheme.Default, slab, buffers, enableFastPaths: false);
        Assert.IsTrue(expected.IsMatch);

        Assert.AreEqual(expected, FzfByteMatcher.FuzzyMatchV2("apricot_pineapple"u8, pattern, false,
            FzfScoringScheme.Default, slab, buffers, enableFastPaths: false));
        Assert.AreEqual("APpL", Encoding.ASCII.GetString(pattern), "Case folding must not mutate the shared query.");
        Assert.IsFalse(FzfByteMatcher.FuzzyMatchV2("apricot_pineapple"u8, pattern, true,
            FzfScoringScheme.Default, slab, buffers).IsMatch);
    }

    [TestMethod]
    public void V1Fallback_StillHonoursCaseSensitivity()
    {
        var text = "a" + new string('x', FzfAlgorithm.MaxV2Cells) + "p";
        var bytes = Encoding.ASCII.GetBytes(text);
        var expected = FzfFuzzyMatcher.FuzzyMatchV1(text, "ap", false, FzfScoringScheme.Default);
        Assert.IsTrue(expected.IsMatch);
        Assert.AreEqual(expected, FzfFuzzyMatcher.FuzzyMatchV2(text, "AP", false, FzfScoringScheme.Default));
        Assert.AreEqual(expected, FzfByteMatcher.FuzzyMatchV2(bytes, "AP"u8.ToArray(), false,
            FzfScoringScheme.Default, new FzfSlab(), new FzfByteBuffers()));
        Assert.IsFalse(FzfFuzzyMatcher.FuzzyMatchV2(text, "AP", true, FzfScoringScheme.Default).IsMatch);
        Assert.IsFalse(FzfByteMatcher.FuzzyMatchV2(bytes, "AP"u8.ToArray(), true,
            FzfScoringScheme.Default, new FzfSlab(), new FzfByteBuffers()).IsMatch);
    }

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
        Assert.IsTrue(lower.IsMatch);
        Assert.AreEqual(lower, upper);

        var lowerByte = FzfByteMatcher.FuzzyMatchV2(
            "apex"u8, [(byte)'a'], caseSensitive: false, FzfScoringScheme.Default,
            new FzfSlab(), new FzfByteBuffers());
        var upperByte = FzfByteMatcher.FuzzyMatchV2(
            "apex"u8, [(byte)'A'], caseSensitive: false, FzfScoringScheme.Default,
            new FzfSlab(), new FzfByteBuffers());
        Assert.IsTrue(lowerByte.IsMatch);
        Assert.AreEqual(lowerByte, upperByte);
    }
}
