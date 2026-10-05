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

    [TestMethod]
    public void GetTranslations_EveryLanguage_ShipsTheIndexNotifications()
    {
        // Rule 18: a user-visible string that reaches the notification card has to exist in all seven
        // languages. A missing key would fall back to the raw key in brackets on screen.
        string[] keys =
        [
            "ContentSearch_NotificationCapReachedTitle",
            "ContentSearch_NotificationCapReachedMessage",
            "ContentSearch_NotificationIndexFinishedTitle",
            "ContentSearch_NotificationIndexFinishedMessage",
            "ContentSearch_NotificationIndexStoppedTitle",
            "ContentSearch_NotificationIndexStoppedMessage",
            "ContentSearch_NotificationIndexPausedTitle",
            "ContentSearch_NotificationIndexPausedMessage"
        ];

        var provider = new ContentSearchTranslationProvider();
        foreach (var culture in provider.SupportedCultures)
        {
            var dict = provider.GetTranslations(culture);
            foreach (var key in keys)
            {
                Assert.IsTrue(dict.ContainsKey(key), $"'{key}' is missing from {culture}");
                Assert.IsFalse(string.IsNullOrWhiteSpace(dict[key]), $"'{key}' is blank in {culture}");
            }
        }
    }

    [TestMethod]
    public void GetTranslations_IndexNotificationMessages_KeepTheFileCountPlaceholder()
    {
        // The count is the actionable part of "indexing finished": a translation that drops {0} would
        // make string.Format return the sentence with a hole in it.
        string[] keys =
        [
            "ContentSearch_NotificationIndexFinishedMessage",
            "ContentSearch_NotificationIndexStoppedMessage",
            "ContentSearch_NotificationIndexPausedMessage"
        ];

        var provider = new ContentSearchTranslationProvider();
        foreach (var culture in provider.SupportedCultures)
        {
            var dict = provider.GetTranslations(culture);
            foreach (var key in keys)
            {
                Assert.Contains("{0}", dict[key], $"'{key}' in {culture} lost its file-count placeholder");
            }
        }
    }
}
