using Lertaro.PluginSdk.Services;
using Lertaro.Plugins.FlowLauncherBridge.Engine;

namespace Lertaro.Plugins.FlowLauncherBridge.Tests.Engine;

[TestClass]
[DoNotParallelize]
public sealed class FlowSettingsNavigationHelperTests
{
    [TestInitialize]
    public void SetUp()
    {
        SettingsSearchService.GetEntriesFunc = () => Array.Empty<SettingsSearchEntryInfo>();
        SettingsWindowService.ShowEntryFunc = null;
        SettingsWindowService.ShowWindowFunc = null;
    }

    [TestCleanup]
    public void TearDown()
    {
        SettingsSearchService.GetEntriesFunc = () => Array.Empty<SettingsSearchEntryInfo>();
        SettingsWindowService.ShowEntryFunc = null;
        SettingsWindowService.ShowWindowFunc = null;
    }

    [TestMethod]
    public void FindPluginConfigEntry_MatchesPluginNameAtBreadcrumbEnd()
    {
        var entries = new[]
        {
            new SettingsSearchEntryInfo("Other", "Plugins › Flow.Launcher 插件桥接 › 配置 › Other", 1),
            new SettingsSearchEntryInfo("启用此插件", "Plugins › Flow.Launcher 插件桥接 › 配置 › Clipboard+", 2)
        };

        var result = FlowSettingsNavigationHelper.FindPluginConfigEntry("clipboard+", entries);

        Assert.IsNotNull(result);
        Assert.AreEqual(2, result.Index);
    }

    [TestMethod]
    public void FindPluginConfigEntry_DoesNotMatchPluginNameInMiddleOfBreadcrumb()
    {
        var entries = new[]
        {
            new SettingsSearchEntryInfo("Clipboard+", "Plugins › Clipboard+ › 配置", 3)
        };

        var result = FlowSettingsNavigationHelper.FindPluginConfigEntry("Clipboard+", entries);

        Assert.IsNull(result);
    }

    [TestMethod]
    public void OpenPluginSettings_NotifiesHostWithMatchingEntry()
    {
        var entry = new SettingsSearchEntryInfo("Enable plugin", "Plugins › Configuration › Clipboard+", 3);
        SettingsSearchService.GetEntriesFunc = () => [entry];
        SettingsSearchEntryInfo? selected = null;
        SettingsWindowService.ShowEntryFunc = item =>
        {
            selected = item;
            return true;
        };

        var result = FlowSettingsNavigationHelper.OpenPluginSettings("Clipboard+");

        Assert.IsTrue(result);
        Assert.IsNotNull(selected);
        Assert.AreEqual(3, selected.Index);
    }

    [TestMethod]
    public void OpenPluginSettings_WithoutMatchingEntryShowsPluginsSection()
    {
        string? selectedSection = null;
        SettingsWindowService.ShowWindowFunc = section =>
        {
            selectedSection = section;
            return true;
        };

        var result = FlowSettingsNavigationHelper.OpenPluginSettings("Missing");

        Assert.IsTrue(result);
        Assert.AreEqual("Plugins", selectedSection);
    }
}
