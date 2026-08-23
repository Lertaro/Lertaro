using Lertaro.Plugins.FlowLauncherBridge.Engine;

namespace Lertaro.Plugins.FlowLauncherBridge.Tests.Engine;

[TestClass]
public sealed class FlowPluginStateStoreTests
{
    [TestMethod]
    public void FlowPluginStateStore_SaveAndLoad_RoundtripsKeywordAndDisabled()
    {
        var testPluginId = "TEST_ID_" + Guid.NewGuid().ToString("N");
        var testPluginName = "TestPlugin_" + Guid.NewGuid().ToString("N");

        FlowPluginStateStore.SaveCustomKeyword(testPluginId, testPluginName, "mykw");
        FlowPluginStateStore.SetPluginDisabled(testPluginId, testPluginName, true);

        var kwById = FlowPluginStateStore.GetCustomKeyword(testPluginId);
        var kwByName = FlowPluginStateStore.GetCustomKeyword("unknown", testPluginName);
        var disabledById = FlowPluginStateStore.IsPluginDisabled(testPluginId);
        var disabledByName = FlowPluginStateStore.IsPluginDisabled("unknown", testPluginName);

        Assert.AreEqual("mykw", kwById);
        Assert.AreEqual("mykw", kwByName);
        Assert.IsTrue(disabledById);
        Assert.IsTrue(disabledByName);
    }
}
