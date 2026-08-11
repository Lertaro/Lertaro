using Lertaro.PluginSdk.Abstractions;

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
}
