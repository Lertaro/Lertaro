using Lertaro.Plugins.FlowLauncherBridge.Engine.JsonRpc;

namespace Lertaro.Plugins.FlowLauncherBridge.Tests.Engine.JsonRpc;

[TestClass]
public sealed class FlowPipManagerTests
{
    [TestMethod]
    public void EnsureFlowEnvironmentStubs_CreatesFlowPluginsDirectoryLayout()
    {
        FlowPipManager.EnsureFlowEnvironmentStubs();

        var flowPluginsDir = FlowPipManager.GetFlowPluginsDirectory();

        var settingsJson = Path.Combine(flowPluginsDir, "Settings", "Settings.json");
        Assert.IsTrue(File.Exists(settingsJson));

        var imagesDir = Path.Combine(flowPluginsDir, "Images");
        Assert.IsTrue(Directory.Exists(imagesDir));
    }
}
