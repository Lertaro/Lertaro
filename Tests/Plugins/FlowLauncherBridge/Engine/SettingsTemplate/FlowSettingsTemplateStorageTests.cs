using Lertaro.Plugins.FlowLauncherBridge.Engine.SettingsTemplate;

namespace Lertaro.Plugins.FlowLauncherBridge.Tests.Engine.SettingsTemplate;

[TestClass]
public sealed class FlowSettingsTemplateStorageTests
{
    private string _tempDir = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "FlowSettingsStorageTest_" + Guid.NewGuid().ToString("N"));
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

    [TestMethod]
    public void EnsureDefaultSettings_PreservesEmptyStringsAndUnescapedNewlines()
    {
        var templatePath = Path.Combine(_tempDir, "SettingsTemplate.yaml");
        var settingsPath = Path.Combine(_tempDir, "Settings.json");

        const string yaml = @"body:
  - type: textarea
    attributes:
      name: services
      defaultValue: ""youdao\ndeepl""
  - type: input
    attributes:
      name: proxyUrl
      defaultValue: ''
  - type: checkbox
    attributes:
      name: enabled
      defaultValue: ""true""
";
        File.WriteAllText(templatePath, yaml);

        FlowSettingsTemplateStorage.EnsureDefaultSettings(templatePath, settingsPath);

        Assert.IsTrue(File.Exists(settingsPath));
        var settings = FlowSettingsTemplateStorage.LoadSettings(settingsPath);
        Assert.AreEqual("youdao\ndeepl", settings["services"]?.ToString());
        Assert.AreEqual("", settings["proxyUrl"]?.ToString());
        Assert.IsTrue(settings["enabled"]?.GetValue<bool>());
    }

    [TestMethod]
    public void SaveSettingValue_CollectionValue_JoinsWithNewline()
    {
        var settingsPath = Path.Combine(_tempDir, "Settings.json");
        var list = new List<string> { "youdao", "deepl", "google" };

        FlowSettingsTemplateStorage.SaveSettingValue(settingsPath, "services", list);

        var val = FlowSettingsTemplateStorage.GetSettingValue(settingsPath, "services");
        Assert.AreEqual("youdao\ndeepl\ngoogle", val?.ToString());
    }

    [TestMethod]
    public void EnsureDefaultSettings_NumericField_SetsInteger()
    {
        var templatePath = Path.Combine(_tempDir, "SettingsTemplate.yaml");
        var settingsPath = Path.Combine(_tempDir, "Settings.json");

        const string yaml = @"body:
  - type: number
    attributes:
      name: timeout
      defaultValue: ""5000""
";
        File.WriteAllText(templatePath, yaml);

        FlowSettingsTemplateStorage.EnsureDefaultSettings(templatePath, settingsPath);

        var settings = FlowSettingsTemplateStorage.LoadSettings(settingsPath);
        Assert.AreEqual(5000, settings["timeout"]?.GetValue<int>());
    }

    [TestMethod]
    public void GetSettingsPath_ReturnsFlatSettingsPath()
    {
        var path = FlowSettingsTemplateStorage.GetSettingsPath(_tempDir, "AudFlow");
        var expectedPath = Path.Combine(_tempDir, "FlowData", "Settings", "AudFlow", "Settings.json");
        Assert.AreEqual(expectedPath, path);
    }
}
