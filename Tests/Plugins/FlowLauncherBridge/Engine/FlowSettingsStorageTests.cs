using Lertaro.Plugins.FlowLauncherBridge.Engine;

namespace Lertaro.Plugins.FlowLauncherBridge.Tests.Engine;

[TestClass]
public sealed class FlowSettingsStorageTests
{
    private string _tempDir = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "LertaroFlowSettingsTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }

    public class SampleConfig
    {
        public string ApiKey { get; set; } = "default_key";
        public int TimeoutSeconds { get; set; } = 30;
    }

    [TestMethod]
    public void LoadSetting_WhenFileDoesNotExist_ReturnsDefaultInstance()
    {
        var storage = new FlowSettingsStorage(_tempDir);
        var config = storage.LoadSetting<SampleConfig>("test-plugin-id");

        Assert.IsNotNull(config);
        Assert.AreEqual("default_key", config.ApiKey);
        Assert.AreEqual(30, config.TimeoutSeconds);
    }

    [TestMethod]
    public void SaveAndLoadSetting_PersistsCorrectly()
    {
        var storage = new FlowSettingsStorage(_tempDir);
        var config = storage.LoadSetting<SampleConfig>("test-plugin-id");
        config.ApiKey = "custom_token_123";
        config.TimeoutSeconds = 60;

        storage.SaveSetting<SampleConfig>("test-plugin-id");
        storage.SaveAll();

        var newStorage = new FlowSettingsStorage(_tempDir);
        var reloaded = newStorage.LoadSetting<SampleConfig>("test-plugin-id");

        Assert.AreEqual("custom_token_123", reloaded.ApiKey);
        Assert.AreEqual(60, reloaded.TimeoutSeconds);
    }

    [TestMethod]
    public void TakeSnapshot_And_RestoreSnapshot_RollsBackInMemoryChanges()
    {
        var storage = new FlowSettingsStorage(_tempDir);
        var config = storage.LoadSetting<SampleConfig>("test-plugin-id");
        config.ApiKey = "initial_key";
        config.TimeoutSeconds = 45;

        var snapshot = storage.TakeSnapshot("test-plugin-id");

        // User edits settings in memory
        config.ApiKey = "modified_key";
        config.TimeoutSeconds = 999;

        // User closes window without confirming -> restore snapshot
        storage.RestoreSnapshot("test-plugin-id", snapshot);

        Assert.AreEqual("initial_key", config.ApiKey);
        Assert.AreEqual(45, config.TimeoutSeconds);
    }
}
