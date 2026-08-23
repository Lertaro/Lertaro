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

    [TestMethod]
    public void ParseYaml_DoubleQuotedWithEscapedNewlines_UnescapesCorrectly()
    {
        const string yaml = @"body:
  - type: textarea
    attributes:
      name: services
      defaultValue: ""youdao\ndeepl\ngoogle\nbing""
";

        var doc = FlowSettingsTemplateParser.ParseContent(yaml, isJson: false);

        Assert.IsNotNull(doc);
        Assert.HasCount(1, doc.Elements);
        Assert.AreEqual("youdao\ndeepl\ngoogle\nbing", doc.Elements[0].DefaultValue);
    }

    [TestMethod]
    public void ParseYaml_MultiTranslateYaml_ParsesAllFieldsAndDescriptions()
    {
        const string yaml = @"body:
  - type: dropdown
    attributes:
      name: interfaceLanguage
      label: Interface Language
      options:
        - English
        - Türkçe
        - 简体中文
      defaultValue: English

  - type: textarea
    attributes:
      name: services
      label: Translate Services
      description: >
        Translation services, one per line.

        Supported services:

        Without configuration:

        Youdao, Google, Baidu, Bing, DeepL
      defaultValue: ""youdao\ndeepl\ngoogle\nbing""

  - type: textarea
    attributes:
      name: serviceConfigs
      label: Service Configs
      description: >
        Config services that require configuration

        e.g. DeepLX, MTranServer, OpenAI

        DeepLX:

        DEEPLX_URL=xxxx
      defaultValue: ''

  - type: input
    attributes:
      name: triggerKeyword
      label: Trigger Keyword
      defaultValue: tr
";

        var doc = FlowSettingsTemplateParser.ParseContent(yaml, isJson: false);

        Assert.IsNotNull(doc);
        Assert.HasCount(4, doc.Elements);

        var lang = doc.Elements[0];
        Assert.AreEqual("dropdown", lang.Type);
        Assert.AreEqual("interfaceLanguage", lang.Name);
        Assert.HasCount(3, lang.Options);
        Assert.AreEqual("English", lang.DefaultValue);

        var services = doc.Elements[1];
        Assert.AreEqual("textarea", services.Type);
        Assert.AreEqual("services", services.Name);
        StringAssert.Contains(services.Description, "Translation services, one per line.");
        StringAssert.Contains(services.Description, "Supported services:");
        StringAssert.Contains(services.Description, "Youdao, Google, Baidu, Bing, DeepL");
        Assert.AreEqual("youdao\ndeepl\ngoogle\nbing", services.DefaultValue);

        var configs = doc.Elements[2];
        Assert.AreEqual("textarea", configs.Type);
        Assert.AreEqual("serviceConfigs", configs.Name);
        StringAssert.Contains(configs.Description, "DEEPLX_URL=xxxx");

        var kw = doc.Elements[3];
        Assert.AreEqual("input", kw.Type);
        Assert.AreEqual("triggerKeyword", kw.Name);
        Assert.AreEqual("tr", kw.DefaultValue);
    }
}
