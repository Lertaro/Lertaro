using System.Text;
using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.PinyinAlias.Tests;

// The provider's own surface after the live switch: the only alias it still emits is the Simplified
// spelling of a Traditional name (the readings are decided by CreateMatcher -- see
// PinyinAliasLiveProviderTests and PinyinTermMatcherTests), and the query-side form mirrors that.
//
// GetAliases' result cache is process-wide and keyed only by text, and PinyinAliasSimplifiedConversionTests
// flips the conversion setting behind it, so this class clears the cache and pins the setting back to its
// default (no GetSettingFunc means the call site's own default) before every test.
[TestClass]
[DoNotParallelize]
public sealed class PinyinAliasProviderTests
{
    private static readonly PinyinAliasProvider Provider = new();

    [TestInitialize]
    public void Reset()
    {
        PluginSettingsService.GetSettingFunc = null;
        PinyinAliasProvider.ResetResultCache();
    }

    private static List<string> DecodeSegments(AliasByteSink sink)
    {
        var decoded = new List<string>(sink.SegmentCount);
        for (var i = 0; i < sink.SegmentCount; i++)
            decoded.Add(Encoding.UTF8.GetString(sink.Segment(i)));
        return decoded;
    }

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
    public void GetAliases_SimplifiedName_ReturnsNothing()
    {
        // The readings are matched live now; a name that is already Simplified contributes no alias.
        Assert.IsEmpty(Provider.GetAliases("中国"));
    }

    [TestMethod]
    public void GetAliases_TraditionalName_ReturnsItsSimplifiedSpelling()
    {
        var aliases = Provider.GetAliases("電腦").ToList();

        Assert.HasCount(1, aliases);
        Assert.AreEqual("电脑", aliases[0]);
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
    public void GetQueryForms_SimplifiedSpellingOnly()
    {
        // A pinyin word gets no rewriting any more (the live matcher reads the typed letters
        // directly), and a Simplified word is a no-op; only a Traditional term gains its form.
        Assert.IsEmpty(Provider.GetQueryForms("zhong"));
        Assert.IsEmpty(Provider.GetQueryForms("创意"));
        CollectionAssert.AreEqual(new[] { "电脑" }, Provider.GetQueryForms("電腦").ToList());
    }

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

    [TestMethod]
    public void GetAliasesUtf8_MatchesGetAliases()
    {
        var text = "電腦";
        var expected = Provider.GetAliases(text).ToList();

        var sink = new AliasByteSink();
        Provider.GetAliasesUtf8(text, sink);

        CollectionAssert.AreEquivalent(expected, DecodeSegments(sink));
    }

    // The host reads "does this alias carry the declared separator" to tell a full reading from an
    // initials shorthand (see AliasMatchRules.TierFor); the Simplified alias is flat either way, but
    // the declaration also drives the alias-side reading of every string tier this provider feeds.
    [TestMethod]
    public void DeclaresTheSyllableSeparatorItsFullReadingActuallyUses()
    {
        Assert.AreEqual(PinyinAliasFormat.SyllableSeparator, Provider.SyllableSeparator);
        Assert.AreNotEqual('\0', Provider.SyllableSeparator, "the host reads '\\0' as 'this provider has no structure'");
    }
}
