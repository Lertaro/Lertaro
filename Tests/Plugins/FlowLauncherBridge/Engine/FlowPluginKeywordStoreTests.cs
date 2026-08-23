using Lertaro.Plugins.FlowLauncherBridge.Engine;

namespace Lertaro.Plugins.FlowLauncherBridge.Tests.Engine;

[TestClass]
public sealed class FlowPluginKeywordStoreTests
{
    [TestMethod]
    public void FlowPluginKeywordStore_SaveAndLoad_RoundtripsKeyword()
    {
        var testPluginId = "TEST_ID_" + Guid.NewGuid().ToString("N");
        var testPluginName = "TestPlugin_" + Guid.NewGuid().ToString("N");

        FlowPluginKeywordStore.SaveCustomKeyword(testPluginId, testPluginName, "mykw");

        var kwById = FlowPluginKeywordStore.GetCustomKeyword(testPluginId);
        var kwByName = FlowPluginKeywordStore.GetCustomKeyword("unknown", testPluginName);

        Assert.AreEqual("mykw", kwById);
        Assert.AreEqual("mykw", kwByName);
    }
}
