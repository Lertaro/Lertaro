using Lertaro.Plugins.ContentSearch.Providers;

namespace Lertaro.Plugins.ContentSearch.Tests.Providers;

[TestClass]
public sealed class ContentSearchTranslationProviderTests
{
    [TestMethod]
    public void SupportedCultures_ContainsAllExpectedLanguages()
    {
        var provider = new ContentSearchTranslationProvider();
        var cultures = provider.SupportedCultures;

        Assert.IsNotNull(cultures);
        Assert.IsTrue(cultures.Contains("zh-CN"));
        Assert.IsTrue(cultures.Contains("en-US"));
        Assert.IsTrue(cultures.Contains("zh-HK"));
        Assert.IsTrue(cultures.Contains("zh-TW"));
        Assert.IsTrue(cultures.Contains("ja-JP"));
        Assert.IsTrue(cultures.Contains("ko-KR"));
        Assert.IsTrue(cultures.Contains("es-ES"));
    }

    [TestMethod]
    public void GetTranslations_ZhCn_ReturnsCorrectTranslations()
    {
        var provider = new ContentSearchTranslationProvider();
        var dict = provider.GetTranslations("zh-CN");

        Assert.IsNotNull(dict);
        Assert.IsTrue(dict.ContainsKey("ContentSearch_PluginName"));
        Assert.AreEqual("内容搜索", dict["ContentSearch_PluginName"]);
        Assert.IsTrue(dict.ContainsKey("ContentSearch_Config_TriggerLabel"));
    }

    [TestMethod]
    public void GetTranslations_EnUs_ReturnsCorrectTranslations()
    {
        var provider = new ContentSearchTranslationProvider();
        var dict = provider.GetTranslations("en-US");

        Assert.IsNotNull(dict);
        Assert.IsTrue(dict.ContainsKey("ContentSearch_PluginName"));
        Assert.AreEqual("Content Search", dict["ContentSearch_PluginName"]);
    }
}
