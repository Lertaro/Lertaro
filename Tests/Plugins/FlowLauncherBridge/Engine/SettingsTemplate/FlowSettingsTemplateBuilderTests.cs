using System.Windows.Controls;
using Lertaro.Plugins.FlowLauncherBridge.Engine.SettingsTemplate;

namespace Lertaro.Plugins.FlowLauncherBridge.Tests.Engine.SettingsTemplate;

[TestClass]
public sealed class FlowSettingsTemplateBuilderTests
{
    [StaTestMethod]
    public void BuildSettingsPanel_ValidYaml_ConstructsPanelAndBindsSettings()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"flow_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var yamlPath = Path.Combine(tempDir, "SettingsTemplate.yaml");
            const string yaml = @"body:
  - type: textBlock
    attributes:
      name: description
      description: >
        Sample Plugin Description
  - type: checkbox
    attributes:
      name: enable_feature
      label: ""Enable Feature""
      defaultValue: ""true""
";
            File.WriteAllText(yamlPath, yaml);

            var settingsPath = Path.Combine(tempDir, "Settings.json");
            File.WriteAllText(settingsPath, @"{ ""enable_feature"": true }");

            var panel = FlowSettingsTemplateBuilder.BuildSettingsPanel(yamlPath, settingsPath);

            Assert.IsNotNull(panel);
            Assert.IsInstanceOfType<ScrollViewer>(panel);

            var scroll = (ScrollViewer)panel;
            Assert.IsInstanceOfType<StackPanel>(scroll.Content);

            var stack = (StackPanel)scroll.Content;
            Assert.HasCount(2, stack.Children);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }
}
