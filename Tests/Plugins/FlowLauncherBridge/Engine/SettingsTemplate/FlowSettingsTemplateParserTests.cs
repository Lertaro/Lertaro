using Lertaro.Plugins.FlowLauncherBridge.Engine.SettingsTemplate;

namespace Lertaro.Plugins.FlowLauncherBridge.Tests.Engine.SettingsTemplate;

[TestClass]
public sealed class FlowSettingsTemplateParserTests
{
    [TestMethod]
    public void ParseYaml_StandardYaml_ParsesElementsCorrectly()
    {
        const string yaml = @"body:
  - type: textBlock
    attributes:
      name: description
      description: >
        Convert between different types of units.
  - type: checkbox
    attributes:
      name: show_helper_text
      label: ""Show helper text of what can be converted""
      defaultValue: ""true""
      description: ""Helpful text""
";

        var doc = FlowSettingsTemplateParser.ParseContent(yaml, isJson: false);

        Assert.IsNotNull(doc);
        Assert.HasCount(2, doc.Elements);

        var elem0 = doc.Elements[0];
        Assert.AreEqual("textblock", elem0.Type.ToLowerInvariant());
        Assert.AreEqual("description", elem0.Name);
        Assert.AreEqual("Convert between different types of units.", elem0.Description);

        var elem1 = doc.Elements[1];
        Assert.AreEqual("checkbox", elem1.Type.ToLowerInvariant());
        Assert.AreEqual("show_helper_text", elem1.Name);
        Assert.AreEqual("Show helper text of what can be converted", elem1.Label);
        Assert.AreEqual("true", elem1.DefaultValue);
        Assert.AreEqual("Helpful text", elem1.Description);
    }

    [TestMethod]
    public void ParseJson_StandardJson_ParsesElementsCorrectly()
    {
        const string json = @"{
  ""body"": [
    {
      ""type"": ""input"",
      ""attributes"": {
        ""name"": ""api_key"",
        ""label"": ""API Key"",
        ""defaultValue"": ""secret""
      }
    }
  ]
}";

        var doc = FlowSettingsTemplateParser.ParseContent(json, isJson: true);

        Assert.IsNotNull(doc);
        Assert.HasCount(1, doc.Elements);

        var elem = doc.Elements[0];
        Assert.AreEqual("input", elem.Type);
        Assert.AreEqual("api_key", elem.Name);
        Assert.AreEqual("API Key", elem.Label);
        Assert.AreEqual("secret", elem.DefaultValue);
    }
}
