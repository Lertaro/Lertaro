using Lertaro.PluginSdk.Services;
using Lertaro.Plugins.FlowLauncherBridge.Engine;

namespace Lertaro.Plugins.FlowLauncherBridge.Tests.Engine;

[TestClass]
public sealed class FlowSettingsNavigationHelperTests
{
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
}
