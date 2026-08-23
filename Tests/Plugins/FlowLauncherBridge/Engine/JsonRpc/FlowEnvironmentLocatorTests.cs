using Lertaro.Plugins.FlowLauncherBridge.Engine.JsonRpc;

namespace Lertaro.Plugins.FlowLauncherBridge.Tests.Engine.JsonRpc;

[TestClass]
public sealed class FlowEnvironmentLocatorTests
{
    [TestMethod]
    public void FindPythonExecutable_DoesNotThrow()
    {
        var python = FlowEnvironmentLocator.FindPythonExecutable();
        // May or may not find Python on machine, but must not crash
        Assert.IsTrue(python == null || python.Length > 0);
    }

    [TestMethod]
    public void FindNodeExecutable_DoesNotThrow()
    {
        var node = FlowEnvironmentLocator.FindNodeExecutable();
        Assert.IsTrue(node == null || node.Length > 0);
    }
}
