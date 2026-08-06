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
    public void GetProviderIdByComponentId_UnknownComponent_ReturnsSentinel255()
    {
        var id = AliasProviderRegistry.GetProviderIdByComponentId("definitely-not-registered::AliasProvider::Nothing");

        Assert.AreEqual((byte)255, id);
    }
}
