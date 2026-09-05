using Lertaro.PluginSdk.Abstractions;
using Lertaro.Plugins.CoreExtensions.Providers.Filters;

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

    [TestMethod]
    public void GetConfigSchema_SearchFiltersGroupContainsBuiltInTogglesAndCustomList()
    {
        var schema = new CoreExtensionsPlugin().GetConfigSchema();
        var searchGroup = schema.Fields.Single(field => field.Key == "SearchFiltersGroup");
        var builtInGroup = searchGroup.SubFields!.Single(field => field.Key == "BuiltInTypeFiltersGroup");
        var customList = searchGroup.SubFields!.Single(field => field.Key == TypeFilterProvider.SidebarCustomFiltersKey);

        CollectionAssert.AreEquivalent(
            new[]
            {
                TypeFilterProvider.DocumentFilterEnabledKey,
                TypeFilterProvider.ImageFilterEnabledKey,
                TypeFilterProvider.VideoFilterEnabledKey
            },
            builtInGroup.SubFields!.Select(field => field.Key).ToList());
        Assert.AreEqual(ConfigFieldType.Array, customList.FieldType);
        Assert.IsNotNull(customList.SubFields);
        CollectionAssert.AreEquivalent(new[] { "Enabled", "Keyword", "Icon", "Rule" }, customList.SubFields!.Select(field => field.Key).ToList());
        var enabled = customList.SubFields!.Single(field => field.Key == "Enabled");
        Assert.AreEqual(ConfigFieldType.Boolean, enabled.FieldType);
        Assert.IsTrue((bool)enabled.DefaultValue!);
    }
}
