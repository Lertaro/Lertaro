using Lertaro.Core.IndexV2.Alias;

namespace Lertaro.Core.Tests.IndexV2.Alias;

// AliasGeneration.Generate exits before touching AliasProviderRegistry for any pure-ASCII/empty name
// (the vectorized HasNonAscii gate) -- fully deterministic without any alias provider registered.
// A non-ASCII name DOES reach AliasProviderRegistry.GetAllProviders(), but since this test process
// never registers one (see AliasProviderRegistryTests), that's also deterministic: no providers means
// no aliases, confirmed below rather than assumed.
[TestClass]
public sealed class AliasGenerationTests
{
    [TestMethod]
    public void Generate_AsciiName_ReturnsNull()
    {
        var result = AliasGeneration.Generate("readme.txt", out var providerIds);

        Assert.IsNull(result);
        Assert.IsEmpty(providerIds);
    }

    [TestMethod]
    public void Generate_EmptyName_ReturnsNull()
    {
        var result = AliasGeneration.Generate("", out _);

        Assert.IsNull(result);
    }

    [TestMethod]
    public void Generate_NonAsciiNameWithNoRegisteredProviders_ReturnsNull()
    {
        var result = AliasGeneration.Generate("文件搜索", out var providerIds);

        Assert.IsNull(result);
        Assert.IsEmpty(providerIds);
    }

    [TestMethod]
    public void Generate_NameWithLoneSurrogate_ReturnsNullBeforeAnyProviderRuns()
    {
        // NTFS file names can legally carry lone surrogate halves; every provider-side Unicode
        // API throws on them, so the gate must reject the name outright instead of letting each
        // provider fail on every scan. This test process registers no providers, so reaching
        // them is also directly observable as an empty result.
        var result = AliasGeneration.Generate("broken\uD83Ename 文件.srt", out var providerIds);

        Assert.IsNull(result);
        Assert.IsEmpty(providerIds);
    }
}
