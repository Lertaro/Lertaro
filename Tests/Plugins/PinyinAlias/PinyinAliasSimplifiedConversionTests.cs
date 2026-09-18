using System.Text;
using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.PinyinAlias.Tests;

// The Traditional/Simplified half of the provider, split from PinyinAliasProviderTests only to keep both
// files under the repo's per-file limit: this class owns the setting-flipping fixtures, and the tests
// left there never touch process-wide state.
//
// PluginSettingsService.GetSettingFunc is a shared static delegate and GetAliases' result cache is
// process-wide, so this class is kept off the parallel schedule and resets both before and after every
// test -- a cache entry baked under the other setting would otherwise answer a test that expected the
// conversion to have been applied or skipped.
[TestClass]
[DoNotParallelize]
public sealed class PinyinAliasSimplifiedConversionTests
{
    private static readonly PinyinAliasProvider Provider = new();

    // A fake converter keeps the DECISION testable without the OS call, which is the untestable half;
    // the real mapping is exercised in HanConversionTests, and the tests that do go through the provider
    // read the expected string from HanConversion rather than hardcoding it.
    private static string FakeConvert(string text) => text
        .Replace('電', '电')
        .Replace('腦', '脑')
        .Replace('體', '体');

    private static void SetConversionEnabled(bool enabled) =>
        PluginSettingsService.GetSettingFunc = (pluginId, key, defaultValue) =>
            pluginId == PinyinAliasConfigSchema.PluginId && key == PinyinAliasConfigSchema.ConvertSettingKey
                ? enabled
                : defaultValue;

    private static void ResetState()
    {
        PluginSettingsService.GetSettingFunc = null;
        PinyinAliasProvider.ResetResultCache();
    }

    [TestInitialize]
    public void Reset() => ResetState();

    [TestCleanup]
    public void Cleanup() => ResetState();

    private static string Cps(string s) => string.Join(",", s.Select(c => ((int)c).ToString("X4")));

    private static List<string> DecodeSegments(AliasByteSink sink)
    {
        var decoded = new List<string>(sink.SegmentCount);
        for (var i = 0; i < sink.SegmentCount; i++)
            decoded.Add(Encoding.UTF8.GetString(sink.Segment(i)));
        return decoded;
    }

    // ── The pure seam ───────────────────────────────────────────────────────

    [TestMethod]
    public void SimplifiedForms_EnabledAndDifferent_YieldsTheConvertedForm() =>
        CollectionAssert.AreEqual(
            new[] { "电脑" },
            PinyinAliasProvider.SimplifiedForms("電腦", true, FakeConvert).ToList());

    [TestMethod]
    public void SimplifiedForms_Disabled_YieldsNothing() =>
        Assert.IsEmpty(PinyinAliasProvider.SimplifiedForms("電腦", false, FakeConvert));

    [TestMethod]
    public void SimplifiedForms_ConversionChangesNothing_YieldsNothing() =>
        // The alias is only worth adding when it actually differs, otherwise every Simplified name would
        // carry a duplicate alias and every Simplified query would gain a duplicate term.
        Assert.IsEmpty(PinyinAliasProvider.SimplifiedForms("电脑", true, FakeConvert));

    [TestMethod]
    public void SimplifiedForms_EmptyText_YieldsNothing()
    {
        Assert.IsEmpty(PinyinAliasProvider.SimplifiedForms("", true, FakeConvert));
        Assert.IsEmpty(PinyinAliasProvider.SimplifiedForms(null!, true, FakeConvert));
    }

    [TestMethod]
    public void SimplifiedForms_ConverterReturnsNull_YieldsNothing() =>
        Assert.IsEmpty(PinyinAliasProvider.SimplifiedForms("電腦", true, _ => null!));

    // ── The provider, through the real converter ───────────────────────────

    [TestMethod]
    public void GetAliases_TraditionalNameWithConversionOn_OffersItsSimplifiedSpellingAndKeepsPinyin()
    {
        SetConversionEnabled(true);
        var expected = HanConversion.ToSimplified("電腦");
        Assert.AreNotEqual("電腦", expected, "the fixture must actually be Traditional for this test to mean anything");

        var aliases = Provider.GetAliases("電腦").ToList();

        CollectionAssert.Contains(aliases, expected);
        Assert.IsTrue(aliases.Any(a => Ascii.IsValid(a)), "the pinyin aliases must still be offered");
    }

    [TestMethod]
    public void GetAliases_TraditionalNameWithConversionOff_OmitsTheSimplifiedSpelling()
    {
        SetConversionEnabled(false);
        var expected = HanConversion.ToSimplified("電腦");

        var aliases = Provider.GetAliases("電腦").ToList();

        CollectionAssert.DoesNotContain(aliases, expected);
        Assert.IsTrue(aliases.Any(a => Ascii.IsValid(a)), "the pinyin aliases must still be offered");
    }

    [TestMethod]
    public void GetQueryForms_TraditionalQueryWithConversionOn_OffersItsSimplifiedSpelling()
    {
        SetConversionEnabled(true);
        var expected = HanConversion.ToSimplified("電腦");

        CollectionAssert.Contains(Provider.GetQueryForms("電腦").ToList(), expected);
    }

    [TestMethod]
    public void GetQueryForms_TraditionalQueryWithConversionOff_OmitsTheSimplifiedSpelling()
    {
        SetConversionEnabled(false);
        var expected = HanConversion.ToSimplified("電腦");

        CollectionAssert.DoesNotContain(Provider.GetQueryForms("電腦").ToList(), expected);
    }

    [TestMethod]
    public void SimplifiedQueryWithConversionOn_MatchesATraditionalNameThroughItsSimplifiedAlias()
    {
        // The Simplified -> Traditional direction, stated as the intersection the matcher actually
        // relies on. The query needs no extra form of its own here: the Traditional name's alias set
        // carries the Simplified spelling, and the query text is compared against those aliases
        // unchanged. GetQueryForms must therefore NOT invent a form for an already-Simplified query --
        // there is nothing to convert, and an identical duplicate would be pure overhead.
        SetConversionEnabled(true);
        var traditional = "個人電腦";
        var simplified = HanConversion.ToSimplified(traditional);
        Assert.AreNotEqual(traditional, simplified, "the fixture must actually be Traditional");

        var aliases = Provider.GetAliases(traditional).ToList();
        var forms = Provider.GetQueryForms(simplified).ToList();

        // The name's alias set is what makes the Simplified term match, and the query adds nothing of its
        // own -- both halves are asserted so a future change to either side is caught.
        CollectionAssert.Contains(aliases, simplified);
        CollectionAssert.DoesNotContain(forms, simplified);
    }

    [TestMethod]
    public void MapAliasToSourceIndices_SimplifiedAlias_MapsEachCharacterToItsSourceChar()
    {
        SetConversionEnabled(true);
        var simplified = HanConversion.ToSimplified("電腦");

        var map = Provider.MapAliasToSourceIndices("電腦", simplified);

        CollectionAssert.AreEqual(new[] { 0, 1 }, map);
    }

    [TestMethod]
    public void MapAliasToSourceIndices_SimplifiedAliasWithConversionOff_ReturnsNull()
    {
        SetConversionEnabled(false);
        var simplified = HanConversion.ToSimplified("電腦");

        Assert.IsNull(Provider.MapAliasToSourceIndices("電腦", simplified));
    }

    [TestMethod]
    public void GetAliasesUtf8_TraditionalName_MatchesGetAliases()
    {
        // The bulk indexing path is the one that carries an alias into the snapshot, so the Simplified
        // form has to reach it too -- a string-path-only implementation would index nothing new.
        SetConversionEnabled(true);
        var expected = Provider.GetAliases("電腦").ToList();

        var sink = new AliasByteSink();
        Provider.GetAliasesUtf8("電腦", sink);

        CollectionAssert.AreEquivalent(expected, DecodeSegments(sink));
    }

    [TestMethod]
    public void GetAliasesUtf8_TraditionalNameWithConversionOff_OmitsTheSimplifiedSpelling()
    {
        SetConversionEnabled(false);
        var simplified = HanConversion.ToSimplified("電腦");

        var sink = new AliasByteSink();
        Provider.GetAliasesUtf8("電腦", sink);

        CollectionAssert.DoesNotContain(DecodeSegments(sink), simplified);
    }
}
