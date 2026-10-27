using Lertaro.PluginSdk.Abstractions;
using Lertaro.Plugins.CoreExtensions.Providers.Filters;
using Lertaro.Plugins.CoreExtensions.Providers.QueryTokens;

namespace Lertaro.Plugins.CoreExtensions.Tests;

[TestClass]
public sealed class CoreExtensionsPluginTests
{
    [TestMethod]
    public void GetActions_RegistersCopyNameImmediatelyBeforeCopyPath()
    {
        var actions = new CoreExtensionsPlugin().GetActions().ToList();

        var copyName = actions.FindIndex(action => action is CoreExtensions.Actions.CopyNameAction);
        var copyPath = actions.FindIndex(action => action is CoreExtensions.Actions.CopyPathAction);
        Assert.AreEqual(copyPath - 1, copyName);
    }

    [TestMethod]
    public void TranslationResources_AreStrictJson()
    {
        // System.Text.Json rejects trailing commas by default; one bad byte silently empties every key
        // and the UI renders placeholders such as [Plugins_StartMenuAppItemProviderName].
        var assembly = typeof(CoreExtensionsPlugin).Assembly;
        foreach (var name in assembly.GetManifestResourceNames()
                     .Where(n => n.Contains(".Translations.", StringComparison.Ordinal) && n.EndsWith(".json", StringComparison.Ordinal)))
        {
            using var stream = assembly.GetManifestResourceStream(name);
            Assert.IsNotNull(stream, name);
            using var reader = new StreamReader(stream!);
            using var _ = System.Text.Json.JsonDocument.Parse(reader.ReadToEnd());
        }
    }

    [TestMethod]
    public void GetConfigSchema_ContainsInlineSearchGroupWithAlwaysOpenFieldEnabledByDefault()
    {
        var plugin = new CoreExtensionsPlugin();
        var schema = plugin.GetConfigSchema();

        Assert.IsNotNull(schema);
        var group = schema.Fields.FirstOrDefault(f => f.Key == "InlineSearchGroup");
        Assert.IsNotNull(group, "Config schema must contain InlineSearchGroup.");
        Assert.AreEqual(ConfigFieldType.Group, group.FieldType);

        var field = group.SubFields?.FirstOrDefault(f => f.Key == "InlineSearchAlwaysOpen");
        Assert.IsNotNull(field, "InlineSearchGroup must contain InlineSearchAlwaysOpen field.");
        Assert.AreEqual(ConfigFieldType.Boolean, field.FieldType);
        Assert.IsTrue((bool)field.DefaultValue!, "InlineSearchAlwaysOpen must default to true.");
    }

    // The inline window's shortcut-command opt-out lives in this plugin's inline section because it applies
    // to the inline window ONLY (see PluginSearchResultMapper): the key and its default are part of that
    // contract, and the App reads them by literal string.
    [TestMethod]
    public void GetConfigSchema_InlineSearchGroup_HasSearchActionsToggleEnabledByDefault()
    {
        var group = new CoreExtensionsPlugin().GetConfigSchema().Fields
            .Single(field => field.Key == "InlineSearchGroup");

        var field = group.SubFields?.FirstOrDefault(f => f.Key == "InlineSearchEnableSearchActions");
        Assert.IsNotNull(field, "InlineSearchGroup must contain InlineSearchEnableSearchActions field.");
        Assert.AreEqual(ConfigFieldType.Boolean, field.FieldType);
        Assert.IsTrue((bool)field.DefaultValue!, "the toggle must default to true so an upgrade changes nothing");
        Assert.IsNotNull(field.LabelKey);
        Assert.IsNotNull(field.DescriptionKey);
    }

    [TestMethod]
    public void GetConfigSchema_SearchFiltersGroupContainsBuiltInTogglesAndCustomList()
    {
        var schema = new CoreExtensionsPlugin().GetConfigSchema();
        var searchGroup = schema.Fields.Single(field => field.Key == "SearchFiltersGroup");
        var builtInGroup = searchGroup.SubFields!.Single(field => field.Key == "BuiltInTypeFiltersGroup");
        // One list for both triggers: the second, look-alike list is gone from the schema, and the surviving
        // one carries the fields that used to belong to it.
        Assert.IsNull(searchGroup.SubFields!.FirstOrDefault(field =>
            field.Key == CustomFilterQueryTokenProvider.LegacySidebarSettingKey),
            "the sidebar list is no longer a settings field");

        var customList = schema.Fields.Single(field => field.Key == "CustomFiltersGroup")
            .SubFields!.Single(field => field.Key == CustomFilterQueryTokenProvider.SettingKey);



        CollectionAssert.AreEquivalent(
            new[]
            {
                TypeFilterProvider.DocumentFilterEnabledKey,
                TypeFilterProvider.ImageFilterEnabledKey,
                TypeFilterProvider.VideoFilterEnabledKey,
                TypeFilterProvider.AudioFilterEnabledKey,
                TypeFilterProvider.ZipFilterEnabledKey,
                TypeFilterProvider.ApplicationFilterEnabledKey,
                TypeFilterProvider.SettingsFilterEnabledKey
            },
            builtInGroup.SubFields!.Select(field => field.Key).ToList());
        Assert.AreEqual(ConfigFieldType.Array, customList.FieldType);
        Assert.IsNotNull(customList.SubFields);
        CollectionAssert.AreEquivalent(new[] { "Enabled", "Keyword", "Label", "Rule", "ShowAsRow", "Icon" }, customList.SubFields!.Select(field => field.Key).ToList());
        var enabled = customList.SubFields!.Single(field => field.Key == "Enabled");
        Assert.AreEqual(ConfigFieldType.Boolean, enabled.FieldType);
        Assert.IsTrue((bool)enabled.DefaultValue!);
    }
}
