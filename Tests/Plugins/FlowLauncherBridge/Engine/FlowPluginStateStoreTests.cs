using Lertaro.Plugins.FlowLauncherBridge.Engine;

namespace Lertaro.Plugins.FlowLauncherBridge.Tests.Engine;

[TestClass]
[DoNotParallelize]
public sealed class FlowPluginStateStoreTests
{
    [TestMethod]
    public void StateSave_PreservesDisabledPluginsUnknownFieldsAndLegacyKeywords()
    {
        var path = Path.Combine(Path.GetTempPath(), $"flow_state_test_{Guid.NewGuid():N}.json");
        FlowPluginStateStore.CustomFilePath = path;
        File.WriteAllText(path, "{\"inactive\":{\"Disabled\":true,\"FutureOption\":42},\"legacy\":\"oldword\"}");
        try
        {
            FlowPluginStateStore.SaveCustomKeyword("active", "newword");
            Assert.IsTrue(FlowPluginStateStore.IsPluginDisabled("inactive"));
            Assert.AreEqual("oldword", FlowPluginStateStore.GetCustomKeyword("legacy"));
            using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
            Assert.AreEqual(42, json.RootElement.GetProperty("inactive").GetProperty("FutureOption").GetInt32());
        }
        finally { FlowPluginStateStore.CustomFilePath = null; File.Delete(path); }
    }

    [TestMethod]
    public void DisabledState_LockedFile_DoesNotPretendSaveSucceeded()
    {
        var path = Path.Combine(Path.GetTempPath(), $"flow_state_test_{Guid.NewGuid():N}.json");
        FlowPluginStateStore.CustomFilePath = path;
        File.WriteAllText(path, "{\"sample\":{\"Disabled\":true}}");
        try
        {
            using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
                Assert.Throws<IOException>(() => FlowPluginStateStore.SetPluginDisabled("sample", false));
            Assert.IsTrue(FlowPluginStateStore.IsPluginDisabled("sample"));
        }
        finally { FlowPluginStateStore.CustomFilePath = null; File.Delete(path); }
    }

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
