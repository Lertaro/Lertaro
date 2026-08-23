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
}
