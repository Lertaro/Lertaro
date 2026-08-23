using Lertaro.Plugins.FlowLauncherBridge.Engine.JsonRpc;

namespace Lertaro.Plugins.FlowLauncherBridge.Tests.Engine.JsonRpc;

[TestClass]
public sealed class FlowPipManagerTests
{
    [TestMethod]
    public void GetFlowPluginsDirectory_ReturnsValidPath()
    {
        var flowPluginsDir = FlowPipManager.GetFlowPluginsDirectory();
        Assert.IsNotNull(flowPluginsDir);
        Assert.IsTrue(flowPluginsDir.EndsWith("FlowPlugins", StringComparison.OrdinalIgnoreCase));
    }
}
