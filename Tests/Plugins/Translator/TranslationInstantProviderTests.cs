using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.Translator.Tests;

[TestClass]
[DoNotParallelize]
public sealed class TranslationInstantProviderTests
{
    private const string PluginId = "Lertaro.Plugins.Translator";

    [TestInitialize]
    public void Reset()
    {
        PluginSettingsService.GetSettingFunc = null;
        PluginSettingsService.NotifySettingChanged(PluginId, "TranslationTrigger");
    }

    [TestMethod]
    public void GetInstantResults_DefaultTriggerWithNoText_ReturnsPlaceholder()
    {
        var result = new TranslationInstantProvider().GetInstantResults("tr ").Single();

        Assert.AreEqual("None", result.ActionType);
    }

    [TestMethod]
    public void GetInstantResults_CustomTriggerWithNoText_ReturnsPlaceholder()
    {
        PluginSettingsService.GetSettingFunc = (pluginId, key, defaultValue) =>
            pluginId == PluginId && key == "TranslationTrigger" ? "translate" : defaultValue;
        PluginSettingsService.NotifySettingChanged(PluginId, "TranslationTrigger");

        var results = new TranslationInstantProvider().GetInstantResults("translate ").ToList();

        Assert.HasCount(1, results);
        Assert.AreEqual("None", results[0].ActionType);
    }

    [TestMethod]
    public void GetInstantResults_QueryWithoutTrigger_ReturnsNothing() =>
        Assert.IsEmpty(new TranslationInstantProvider().GetInstantResults("hello"));
}
