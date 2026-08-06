using Lertaro.Core.SearchIndex;

namespace Lertaro.Core.Tests.SearchIndex;

// MixedQueryMatcher.TrySegment iterates AliasProviderRegistry.GetActiveProviders() -- this test process
// never registers any (no plugins loaded), so it deterministically returns null for any input without
// needing to touch (or pollute) that shared static registry. Deeper mixed-alphabet segmentation logic
// requires a real IAliasProvider and isn't covered here for that reason (see AliasProviderRegistryTests).
[TestClass]
public sealed class MixedQueryMatcherTests
{
    [TestMethod]
    public void TrySegment_NoRegisteredProviders_ReturnsNull() => Assert.IsNull(MixedQueryMatcher.TrySegment("anything"));

    [TestMethod]
    public void TrySegment_EmptyTerm_ReturnsNull() => Assert.IsNull(MixedQueryMatcher.TrySegment(""));
}
