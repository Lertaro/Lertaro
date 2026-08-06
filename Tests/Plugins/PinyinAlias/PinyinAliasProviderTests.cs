using System.Text;
using Lertaro.PluginSdk.Abstractions.Plugins;

namespace Lertaro.Plugins.PinyinAlias.Tests;

[TestClass]
public sealed class PinyinAliasProviderTests
{
    private static readonly PinyinAliasProvider Provider = new();

    [TestMethod]
    public void CanHandle_ContainsChinese_ReturnsTrue() => Assert.IsTrue(Provider.CanHandle("hello 中文"));

    [TestMethod]
    public void CanHandle_PureAscii_ReturnsFalse() => Assert.IsFalse(Provider.CanHandle("hello world"));

    [TestMethod]
    public void CanHandle_EmptyOrNull_ReturnsFalse()
    {
        Assert.IsFalse(Provider.CanHandle(""));
        Assert.IsFalse(Provider.CanHandle(null!));
    }

    [TestMethod]
    public void GetAliases_ChineseText_ReturnsNonEmptyResults()
    {
        var aliases = Provider.GetAliases("中国").ToList();

        Assert.IsNotEmpty(aliases);
    }

    [TestMethod]
    public void GetAliases_CalledTwiceWithSameText_ReturnsCachedSameReference()
    {
        var first = Provider.GetAliases("中国人民");
        var second = Provider.GetAliases("中国人民");

        Assert.AreSame(first, second);
    }

    [TestMethod]
    public void GetAliases_EmptyText_ReturnsEmpty() => Assert.IsEmpty(Provider.GetAliases(""));

    [TestMethod]
    public void MapAliasToSourceIndices_InitialsAlias_ReturnsIdentityMapping()
    {
        var map = Provider.MapAliasToSourceIndices("中国", "zg");

        Assert.IsNotNull(map);
        CollectionAssert.AreEqual(new[] { 0, 1 }, map);
    }

    [TestMethod]
    public void MapAliasToSourceIndices_FullPinyinAlias_MapsEachSyllableToItsSourceChar()
    {
        var map = Provider.MapAliasToSourceIndices("中国", "zhong" + PinyinAliasFormat.SyllableSeparator + "guo");

        Assert.IsNotNull(map);
        // "zhong" (5 letters) -> source char 0; the boundary maps to the character it introduces, so
        // a match spanning it highlights both syllables rather than dropping one; "guo" -> source char 1.
        CollectionAssert.AreEqual(new[] { 0, 0, 0, 0, 0, 1, 1, 1, 1 }, map);
    }

    [TestMethod]
    public void MapAliasToSourceIndices_FullPinyinWithoutItsBoundaries_ReturnsNull() =>
        // The undelimited form is no longer something this provider produces for this text, and the
        // contract is to report that rather than guess -- a wrong map silently mis-highlights.
        Assert.IsNull(Provider.MapAliasToSourceIndices("中国", "zhongguo"));

    [TestMethod]
    public void MapAliasToSourceIndices_EmptyInputs_ReturnsNull()
    {
        Assert.IsNull(Provider.MapAliasToSourceIndices("", "zg"));
        Assert.IsNull(Provider.MapAliasToSourceIndices("中国", ""));
    }

    [TestMethod]
    public void MapAliasToSourceIndices_UnmatchableAlias_ReturnsNull() => Assert.IsNull(Provider.MapAliasToSourceIndices("中国", "xyz123"));

    // Regression test for a real bug: a browser tab title like "example.com | 代理" has a literal '|'
    // before the Chinese portion. Before the fix, that '|' passed straight through into the generated
    // alias, got misread by every downstream consumer as an alternative-reading boundary, and the
    // Chinese portion's highlight (and, depending on positioning, its match) silently disappeared.
    [TestMethod]
    public void GetAliases_TextWithLiteralPipe_NeverContainsLiteralPipeCharacter()
    {
        var aliases = Provider.GetAliases("id | 中").ToList();

        foreach (var alias in aliases)
            Assert.DoesNotContain("|", alias);
    }

    [TestMethod]
    public void MapAliasToSourceIndices_TextWithLiteralPipe_StillMapsChinesePortionToItsSourceChar()
    {
        var text = "id | 中";
        var fullAlias = Provider.GetAliases(text).First(a => a.EndsWith("zhong", StringComparison.Ordinal));

        var map = Provider.MapAliasToSourceIndices(text, fullAlias);

        Assert.IsNotNull(map);
        // "中" is the last character of `text` (source index 5) -- every position of the trailing
        // "zhong" syllable in the alias must map back to that one source character.
        for (var i = map.Length - 5; i < map.Length; i++)
            Assert.AreEqual(5, map[i]);
    }

    [TestMethod]
    public void GetAliasesUtf8_MatchesGetAliases()
    {
        var text = "中国人民";
        var expected = Provider.GetAliases(text).ToList();

        var sink = new AliasByteSink();
        Provider.GetAliasesUtf8(text, sink);
        var decoded = new List<string>(sink.SegmentCount);
        for (var i = 0; i < sink.SegmentCount; i++)
            decoded.Add(Encoding.UTF8.GetString(sink.Segment(i)));

        CollectionAssert.AreEquivalent(expected, decoded);
    }
}
