using Lertaro.Plugins.FlowLauncherBridge.Engine;

namespace Lertaro.Plugins.FlowLauncherBridge.Tests.Engine;

[TestClass]
public sealed class FlowLauncherBridgeEnablementTests
{
    [TestMethod]
    public void IsRuntimeEnabled_StaysEnabledWhileEitherHostComponentIsEnabled()
    {
        var enabledComponents = new HashSet<string>();
        bool IsEnabled(string dll, string type, string name) => enabledComponents.Contains(type);

        Assert.IsFalse(FlowLauncherBridgeEnablement.IsRuntimeEnabled(IsEnabled, "FlowLauncherBridge.dll"));

        enabledComponents.Add("InstantProvider");
        Assert.IsTrue(FlowLauncherBridgeEnablement.IsRuntimeEnabled(IsEnabled, "FlowLauncherBridge.dll"));

        enabledComponents.Clear();
        enabledComponents.Add("FilePreviewProvider");
        Assert.IsTrue(FlowLauncherBridgeEnablement.IsRuntimeEnabled(IsEnabled, "FlowLauncherBridge.dll"));
    }
}
