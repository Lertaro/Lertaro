using Lertaro.Plugins.FlowLauncherBridge.Engine.JsonRpc;

namespace Lertaro.Plugins.FlowLauncherBridge.Tests.Engine.JsonRpc;

[TestClass]
public sealed class FlowPipManagerTests
{
    [TestMethod]
    public void GetFlowDataAndPluginsDirectory_ReturnsValidPaths()
    {
        var flowDataDir = FlowPipManager.GetFlowDataDirectory();
        var flowPluginsDir = FlowPipManager.GetFlowPluginsDirectory();

        Assert.IsNotNull(flowDataDir);
        Assert.IsTrue(flowDataDir.EndsWith("FlowData", StringComparison.OrdinalIgnoreCase));

        Assert.IsNotNull(flowPluginsDir);
        Assert.IsTrue(flowPluginsDir.EndsWith(Path.Combine("FlowData", "Plugins"), StringComparison.OrdinalIgnoreCase));
    }
}
