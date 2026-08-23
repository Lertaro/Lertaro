using Flow.Launcher.Plugin;
using Lertaro.PluginSdk.Abstractions;
using Lertaro.Plugins.FlowLauncherBridge.Engine;
using Lertaro.Plugins.FlowLauncherBridge.Engine.SettingsTemplate;

namespace Lertaro.Plugins.FlowLauncherBridge.Tests.Engine.SettingsTemplate;

[TestClass]
public sealed class FlowConfigSchemaBuilderTests
{
    [TestMethod]
    public void BuildSchema_WithYamlTemplate_GroupsFieldsUnderPluginGroup()
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
      description: Sample Plugin Description
  - type: checkbox
    attributes:
      name: enable_feature
      label: ""Enable Feature""
      defaultValue: ""true""
  - type: txtbox
    attributes:
      name: api_key
      label: ""API Key""
      defaultValue: ""secret""
  - type: select
    attributes:
      name: theme_choice
      label: ""Theme""
      defaultValue: ""Dark""
      options:
        - Light
        - Dark
        - System
";
            File.WriteAllText(yamlPath, yaml);

            var metadata = new PluginMetadata
            {
                ID = "TEST_YAML_PLUGIN",
                Name = "TestYamlPlugin",
                PluginDirectory = tempDir
            };

            var storage = new FlowSettingsStorage(tempDir);
            var host = new FlowPluginHost(storage, [tempDir]);

            var pair = new PluginPair { Metadata = metadata };
            host.RegisterPlugin(pair);

            var schema = FlowConfigSchemaBuilder.BuildSchema(host);

            Assert.IsNotNull(schema);
            Assert.IsNotNull(schema.Fields);
            Assert.HasCount(2, schema.Fields);

            var triggerField = schema.Fields.FirstOrDefault(f => f.Key == "TriggerKeyword");
            Assert.IsNotNull(triggerField);

            var groupField = schema.Fields.FirstOrDefault(f => f.Key == "TestYamlPluginGroup");
            Assert.IsNotNull(groupField);
            Assert.AreEqual(ConfigFieldType.Group, groupField.FieldType);
            Assert.AreEqual("TestYamlPlugin", groupField.LabelKey);
            Assert.IsNotNull(groupField.SubFields);
            Assert.HasCount(3, groupField.SubFields);

            var checkboxField = groupField.SubFields.FirstOrDefault(f => f.Key == "TestYamlPlugin.enable_feature");
            Assert.IsNotNull(checkboxField);
            Assert.AreEqual(ConfigFieldType.Boolean, checkboxField.FieldType);
            Assert.IsTrue((bool)checkboxField.DefaultValue);
            Assert.AreEqual("Enable Feature", checkboxField.LabelKey);

            var txtField = groupField.SubFields.FirstOrDefault(f => f.Key == "TestYamlPlugin.api_key");
            Assert.IsNotNull(txtField);
            Assert.AreEqual(ConfigFieldType.Text, txtField.FieldType);
            Assert.AreEqual("secret", txtField.DefaultValue);

            var selectField = groupField.SubFields.FirstOrDefault(f => f.Key == "TestYamlPlugin.theme_choice");
            Assert.IsNotNull(selectField);
            Assert.AreEqual(ConfigFieldType.Choice, selectField.FieldType);
            Assert.IsNotNull(selectField.Choices);
            Assert.HasCount(3, selectField.Choices);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }
}
