using Lertaro.Core.IndexV2.Alias;

namespace Lertaro.Core.Tests.IndexV2.Alias;

// Same reasoning as AliasGenerationTests: no alias providers are registered in this test process, so
// both the ASCII fast-path and the (empty) provider loop are fully deterministic here.
[TestClass]
public sealed class AliasGenerationUtf8Tests
{
    [TestMethod]
    public void Generate_AsciiName_ReturnsNull()
    {
        ulong mask = 0;
        var result = AliasGenerationUtf8.Generate("readme.txt", ref mask);

        Assert.IsNull(result);
        Assert.AreEqual(0UL, mask);
    }

    [TestMethod]
    public void Generate_EmptyName_ReturnsNull()
    {
        ulong mask = 0;
        var result = AliasGenerationUtf8.Generate("", ref mask);

        Assert.IsNull(result);
    }

    [TestMethod]
    public void Generate_NonAsciiNameWithNoRegisteredProviders_ReturnsNull()
    {
        ulong mask = 0;
        var result = AliasGenerationUtf8.Generate("文件搜索", ref mask);

        Assert.IsNull(result);
    }

    [TestMethod]
    public void Generate_NameWithLoneSurrogate_ReturnsNullBeforeAnyProviderRuns()
    {
        // Mirrors the AliasGeneration gate: lone surrogates are rejected before any provider runs.
        ulong mask = 0;
        var result = AliasGenerationUtf8.Generate("broken\uD83Ename 文件.srt", ref mask);

        Assert.IsNull(result);
    }
}
