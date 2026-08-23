using Flow.Launcher.Plugin;
using Lertaro.PluginSdk.Services;
using Lertaro.Plugins.FlowLauncherBridge.Engine;
using Lertaro.Plugins.FlowLauncherBridge.Providers;

namespace Lertaro.Plugins.FlowLauncherBridge.Tests.Providers;

[TestClass]
[DoNotParallelize]
public sealed class FlowCommunityUninstallHelperTests
{
    private string _tempDir = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "LertaroFlowUninstallTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        TranslationService.LookupFunc = key => key;
    }

    [TestCleanup]
    public void Cleanup()
    {
        TranslationService.LookupFunc = key => $"[{key}]";
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }

    [TestMethod]
    public void QueryInstalledPluginsForUninstall_WhenEmpty_ReturnsNoInstalledItem()
    {
        var storage = new FlowSettingsStorage(_tempDir);
        var host = new FlowPluginHost(storage, []);

        var results = FlowCommunityUninstallHelper.QueryInstalledPluginsForUninstall(host, "").ToList();

        Assert.HasCount(1, results);
        Assert.AreEqual("FlowLauncherBridge_NoInstalledPluginsTitle", results[0].Title);
    }

    [TestMethod]
    public void QueryInstalledPluginsForUninstall_WhenPluginsExist_ReturnsUninstallableItems()
    {
        var storage = new FlowSettingsStorage(_tempDir);
        var host = new FlowPluginHost(storage, []);
        var pair = new PluginPair
        {
            Metadata = new PluginMetadata { ID = "unl-1", Name = "Calculator", Version = "1.0.0", Description = "Smart calc" }
        };
        host.RegisterPlugin(pair);

        var results = FlowCommunityUninstallHelper.QueryInstalledPluginsForUninstall(host, "").ToList();

        Assert.HasCount(1, results);
        Assert.Contains("Calculator", results[0].Title);
        Assert.Contains("FlowLauncherBridge_Uninstall", results[0].Title);
        Assert.AreEqual("Execute", results[0].ActionType);
        Assert.IsNotNull(results[0].OnExecute);
    }
}
