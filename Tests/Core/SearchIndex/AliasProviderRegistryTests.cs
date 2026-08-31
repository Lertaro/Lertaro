using Lertaro.Core.SearchIndex;

namespace Lertaro.Core.Tests.SearchIndex;

// AliasProviderRegistry.Register adds to a single process-wide ConcurrentBag with no reset/unregister
// hook -- calling it here would leak a fake provider into every other test's registry state for the
// rest of the process (tests run in one shared AppDomain, with method-level parallelism enabled). So
// this only covers the pure, registration-independent members; Register/GetActiveProviders/
// GetAllProviders/ComputeProvidersFingerprint are exercised indirectly wherever real plugins register.
[TestClass]
public sealed class AliasProviderRegistryTests
{
    [TestMethod]
    [DataRow("readme", false)]
    [DataRow("文件搜索", true)]
    [DataRow("café", true)]
    [DataRow("", false)]
    public void HasNonAscii_DetectsAnyNonAsciiCharacter(string text, bool expected) => Assert.AreEqual(expected, AliasProviderRegistry.HasNonAscii(text));

    [TestMethod]
    [DataRow("readme", false)]
    [DataRow("文件搜索", false)]
    [DataRow("\U0001F872", false)] // a complete surrogate pair is valid UTF-16
    [DataRow("name \U0001F872.txt", false)]
    [DataRow("", false)]
    public void HasInvalidUtf16_DetectsUnpairedSurrogatesOnly(string text, bool expected) =>
        Assert.AreEqual(expected, AliasProviderRegistry.HasInvalidUtf16(text));

    [TestMethod]
    public void HasInvalidUtf16_LoneSurrogates_ReturnsTrue()
    {
        // Not DataRows: DataRow's parameter plumbing replaces lone surrogate halves with
        // U+FFFD before the test method ever sees them, so the gate would see valid text.
        Assert.IsTrue(AliasProviderRegistry.HasInvalidUtf16("\uD83E"), "lone high surrogate");
        Assert.IsTrue(AliasProviderRegistry.HasInvalidUtf16("\uDED2"), "lone low surrogate");
        Assert.IsTrue(AliasProviderRegistry.HasInvalidUtf16("name\uD83E.txt"), "high surrogate followed by a BMP char");
    }

    [TestMethod]
    public void GetProviderIdByComponentId_UnknownComponent_ReturnsSentinel255()
    {
        var id = AliasProviderRegistry.GetProviderIdByComponentId("definitely-not-registered::AliasProvider::Nothing");

        Assert.AreEqual((byte)255, id);
    }
}
