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
            Assert.HasCount(6, groupField.SubFields);

            var enabledField = groupField.SubFields.FirstOrDefault(f => f.Key == "TestYamlPlugin.Enabled");
            Assert.IsNotNull(enabledField);
            Assert.AreEqual(ConfigFieldType.Boolean, enabledField.FieldType);
            Assert.IsTrue((bool)enabledField.DefaultValue);

            var kwField = groupField.SubFields.FirstOrDefault(f => f.Key == "TestYamlPlugin.ActionKeyword");
            Assert.IsNotNull(kwField);
            Assert.AreEqual(ConfigFieldType.Text, kwField.FieldType);

            var descField = groupField.SubFields.FirstOrDefault(f => f.Key == "TestYamlPlugin.description");
            Assert.IsNotNull(descField);
            Assert.AreEqual("Sample Plugin Description", descField.DescriptionKey);

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

    [TestMethod]
    public void BuildSchema_PluginWithoutSettingsTemplate_GetsActionKeywordField()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"flow_test_kw_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var metadata = new PluginMetadata
            {
                ID = "WEATHER_PLUGIN",
                Name = "Weather",
                ActionKeyword = "w",
                ActionKeywords = ["w"],
                PluginDirectory = tempDir
            };

            var storage = new FlowSettingsStorage(tempDir);
            var host = new FlowPluginHost(storage, [tempDir]);

            var pair = new PluginPair { Metadata = metadata };
            host.RegisterPlugin(pair);

            var schema = FlowConfigSchemaBuilder.BuildSchema(host);

            Assert.IsNotNull(schema);
            var groupField = schema.Fields.FirstOrDefault(f => f.Key == "WeatherGroup");
            Assert.IsNotNull(groupField);
            Assert.IsNotNull(groupField.SubFields);
            Assert.HasCount(2, groupField.SubFields);

            var enabledField = groupField.SubFields[0];
            Assert.AreEqual("Weather.Enabled", enabledField.Key);
            Assert.AreEqual(ConfigFieldType.Boolean, enabledField.FieldType);

            var kwField = groupField.SubFields[1];
            Assert.AreEqual("Weather.ActionKeyword", kwField.Key);
            Assert.AreEqual(ConfigFieldType.Text, kwField.FieldType);
            Assert.AreEqual("w", kwField.DefaultValue);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [TestMethod]
    public void BuildSchema_WithSettingProviderPlugin_AddsCustomControlField()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"flow_test_sp_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var metadata = new PluginMetadata
            {
                ID = "MDICT_PLUGIN",
                Name = "MDict",
                ActionKeyword = "md",
                ActionKeywords = ["md"],
                PluginDirectory = tempDir
            };

            var storage = new FlowSettingsStorage(tempDir);
            var host = new FlowPluginHost(storage, [tempDir]);

            var fakePlugin = new FakeSettingProviderPlugin();
            var pair = new PluginPair { Metadata = metadata, Plugin = fakePlugin };
            host.RegisterPlugin(pair);

            var t = new Thread(() =>
            {
                var schema = FlowConfigSchemaBuilder.BuildSchema(host);

                Assert.IsNotNull(schema);
                var groupField = schema.Fields.FirstOrDefault(f => f.Key == "MDictGroup");
                Assert.IsNotNull(groupField);
                Assert.IsNotNull(groupField.SubFields);
                Assert.HasCount(3, groupField.SubFields);

                var customControlField = groupField.SubFields.FirstOrDefault(f => f.Key == "MDict.CustomPanel");
                Assert.IsNotNull(customControlField);
                Assert.AreEqual(ConfigFieldType.CustomControl, customControlField.FieldType);
                Assert.IsNotNull(customControlField.CustomControl);
            });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join();
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    private sealed class FakeSettingProviderPlugin : IPlugin, ISettingProvider
    {
        public void Init(PluginInitContext context) { }
        public List<Result> Query(Query query) => [];
        public System.Windows.Controls.Control CreateSettingPanel() => new System.Windows.Controls.UserControl { Content = new System.Windows.Controls.TextBlock { Text = "Settings" } };
    }
}
