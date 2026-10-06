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
    public void SaveAll_PreservesUnknownRootFieldsFromOtherPluginVersions()
    {
        var folder = Directory.CreateDirectory(Path.Combine(_tempDir, "sample")).FullName;
        var path = Path.Combine(folder, "SampleConfig.json");
        File.WriteAllText(path, "{\"ApiKey\":\"old\",\"FutureOption\":42}");
        var storage = new FlowSettingsStorage(_tempDir);
        storage.LoadSetting<SampleConfig>("sample").ApiKey = "new";
        storage.SaveAll();
        using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
        Assert.AreEqual("new", json.RootElement.GetProperty("ApiKey").GetString());
        Assert.AreEqual(42, json.RootElement.GetProperty("FutureOption").GetInt32());
    }

    [TestMethod]
    public void UnderscoreNames_KeepSeparateFilesAndSnapshots()
    {
        var storage = new FlowSettingsStorage(_tempDir);
        storage.LoadSetting<SampleConfig>("Name").ApiKey = "plain";
        storage.LoadSetting<SampleConfig>("Name_With_Underscore").ApiKey = "underscore";
        Assert.HasCount(1, storage.TakeSnapshot("Name"));
        storage.SaveAll();
        Assert.IsTrue(File.Exists(Path.Combine(_tempDir, "Name_With_Underscore", "SampleConfig.json")));
        var reloaded = new FlowSettingsStorage(_tempDir);
        Assert.AreEqual("plain", reloaded.LoadSetting<SampleConfig>("Name").ApiKey);
        Assert.AreEqual("underscore", reloaded.LoadSetting<SampleConfig>("Name_With_Underscore").ApiKey);
    }

    [TestMethod]
    public void SaveAll_LockedFile_ReportsFailureAndKeepsPreviousBytes()
    {
        var storage = new FlowSettingsStorage(_tempDir);
        var config = storage.LoadSetting<SampleConfig>("sample");
        storage.SaveAll();
        var path = Path.Combine(_tempDir, "sample", "SampleConfig.json");
        var previous = File.ReadAllText(path);
        config.ApiKey = "new";
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.Throws<IOException>(() => storage.SaveAll());
        Assert.AreEqual(previous, File.ReadAllText(path));
        Assert.IsEmpty(Directory.GetFiles(_tempDir, "*.tmp", SearchOption.AllDirectories));
    }

    [TestMethod]
    public void LoadSetting_CorruptFile_DoesNotReturnWritableDefaults()
    {
        var folder = Directory.CreateDirectory(Path.Combine(_tempDir, "sample")).FullName;
        var path = Path.Combine(folder, "SampleConfig.json");
        File.WriteAllText(path, "null");
        Assert.Throws<InvalidDataException>(() => new FlowSettingsStorage(_tempDir).LoadSetting<SampleConfig>("sample"));
        Assert.AreEqual("null", File.ReadAllText(path));
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

    [TestMethod]
    public void GetPluginSettingsDirectory_DefaultConstructor_ResolvesUnderPluginSettingsFolder()
    {
        var storage = new FlowSettingsStorage();
        var dir = storage.GetPluginSettingsDirectory("my-plugin");

        Assert.IsTrue(dir.Contains(Path.Combine("FlowData", "Settings", "Plugins", "my-plugin"), StringComparison.OrdinalIgnoreCase));
    }
}
