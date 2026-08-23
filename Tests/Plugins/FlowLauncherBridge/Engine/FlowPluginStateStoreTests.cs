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
            var testPluginName = "TestPlugin_" + Guid.NewGuid().ToString("N");

            FlowPluginStateStore.SaveCustomKeyword(testPluginName, "mykw");
            FlowPluginStateStore.SetPluginDisabled(testPluginName, true);

            var kwByName = FlowPluginStateStore.GetCustomKeyword(testPluginName);
            var disabledByName = FlowPluginStateStore.IsPluginDisabled(testPluginName);

            Assert.AreEqual("mykw", kwByName);
            Assert.IsTrue(disabledByName);

            var all = FlowPluginStateStore.LoadAll();
            Assert.IsTrue(all.ContainsKey(testPluginName));
        }
        finally
        {
            FlowPluginStateStore.CustomFilePath = null;
            try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { }
        }
    }
}
