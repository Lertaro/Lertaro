using Lertaro.Core.SearchIndex;
using Lertaro.PluginSdk.Abstractions.Plugins;

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

    [TestMethod]
    public void GetProviderId_UnknownProvider_ReturnsTheSameSentinelAsTheComponentIdLookup()
    {
        // The two lookups used to disagree: this one answered 0, which is also the id the FIRST registered
        // provider receives, so an unresolved lookup was indistinguishable from a genuine provider-0 hit --
        // and every caller tests the result against SearchContext.DisabledAliasIds, so an unregistered
        // provider was judged by provider 0's enabled state instead of its own.
        var id = AliasProviderRegistry.GetProviderId(new NeverRegisteredProvider());

        Assert.AreEqual((byte)255, id, "0 was the old answer, and 0 is a valid provider id");
        Assert.AreEqual(AliasProviderRegistry.GetProviderIdByComponentId("no-such-component::AliasProvider::Nothing"), id);
    }

    // Deliberately NOT registered: this file's whole policy (see the header) is that Register leaks a
    // provider into every other test's process-wide registry state, so the id-allocation and visibility
    // order that Register now establishes under its own lock are pinned structurally rather than here.
    private sealed class NeverRegisteredProvider : IAliasProvider
    {
        public bool CanHandle(string text) => false;
        public IEnumerable<string> GetAliases(string text) => Array.Empty<string>();
        public IReadOnlyList<(char Start, char End)> InputRanges { get; } = Array.Empty<(char, char)>();
        public IReadOnlyList<(char Start, char End)> OutputRanges { get; } = Array.Empty<(char, char)>();
    }
}
