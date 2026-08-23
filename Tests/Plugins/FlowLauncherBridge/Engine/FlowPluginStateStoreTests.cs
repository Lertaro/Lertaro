using Lertaro.Plugins.FlowLauncherBridge.Engine;

namespace Lertaro.Plugins.FlowLauncherBridge.Tests.Engine;

[TestClass]
[DoNotParallelize]
public sealed class FlowPluginStateStoreTests
{
    [TestMethod]
    public void FlowPluginStateStore_SaveAndLoad_RoundtripsKeywordAndDisabled()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"flow_state_test_{Guid.NewGuid():N}.json");
        FlowPluginStateStore.CustomFilePath = tempFile;

        try
        {
            var testPluginId = "TEST_ID_" + Guid.NewGuid().ToString("N");
            var testPluginName = "TestPlugin_" + Guid.NewGuid().ToString("N");

            FlowPluginStateStore.SaveCustomKeyword(testPluginId, testPluginName, "mykw");
            FlowPluginStateStore.SetPluginDisabled(testPluginId, testPluginName, true);

            var kwByName = FlowPluginStateStore.GetCustomKeyword(testPluginId, testPluginName);
            var disabledByName = FlowPluginStateStore.IsPluginDisabled(testPluginId, testPluginName);

            Assert.AreEqual("mykw", kwByName);
            Assert.IsTrue(disabledByName);

            // Verify that Plugins.json only contains the unified Name key, not the ID
            var all = FlowPluginStateStore.LoadAll();
            Assert.IsTrue(all.ContainsKey(testPluginName));
            Assert.IsFalse(all.ContainsKey(testPluginId));
        }
        finally
        {
            FlowPluginStateStore.CustomFilePath = null;
            try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { }
        }
    }
}
