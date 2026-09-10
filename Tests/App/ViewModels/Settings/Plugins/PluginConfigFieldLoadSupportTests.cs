using Lertaro.App.ViewModels.Settings.Plugins;
using Lertaro.Core;
using Lertaro.PluginSdk.Abstractions;

namespace Lertaro.App.Tests.ViewModels.Settings.Plugins;

// Child/array rows are built on first access rather than in the constructor. That is what lets the whole
// plugin list be constructed without paying for every plugin's config tree -- the page only ever shows
// one plugin's form, and building the rest was the bulk of its first-open cost. Lives in its own file
// because PluginConfigFieldViewModelTests had crossed the repo's per-file line limit, matching how the
// production side split its own load logic into PluginConfigFieldLoadSupport.
[TestClass]
public sealed class PluginConfigFieldLoadSupportTests
{
    private static PluginConfigField TextField(string key, string defaultValue) => new()
    {
        Key = key,
        FieldType = ConfigFieldType.Text,
        DefaultValue = defaultValue,
    };

    private static PluginConfigField Group(params PluginConfigField[] children) => new()
    {
        Key = "group",
        FieldType = ConfigFieldType.Group,
        SubFields = children.ToList(),
    };

    private static PluginConfigFieldViewModel Vm(PluginConfigField field, Action? onValueChanged = null, UserSettings? settings = null) =>
        new("plugin", field, settings ?? new UserSettings(), onValueChanged);

    [TestMethod]
    public void GroupChildren_LoadOnAccessAndStayLoaded()
    {
        var vm = Vm(Group(TextField("child", "child-default")));

        Assert.HasCount(1, vm.Children);
        Assert.AreEqual("child-default", vm.Children[0].Value);
    }

    [TestMethod]
    public void GroupChildrenAccess_ReturnsTheSameInstanceEachTime()
    {
        var vm = Vm(Group(TextField("child", "")));

        Assert.AreSame(vm.Children, vm.Children,
            "bindings hold the collection instance, so it must not be replaced");
    }

    [TestMethod]
    public void GroupChildren_LoadEvenWhenTheTreeWasNeverTouchedBefore()
    {
        // Repeated access must not re-run the load or duplicate rows.
        var vm = Vm(Group(TextField("a", ""), TextField("b", "")));

        Assert.HasCount(2, vm.Children);
        Assert.HasCount(2, vm.Children);
    }

    [TestMethod]
    public void FieldOwningAChangeCallback_StillBuildsNoChildren()
    {
        // Array-item sub-fields and object children carry a change callback; the constructor this lazy
        // path replaced never built children for them, and the getter must not either.
        var vm = Vm(Group(TextField("child", "c")), onValueChanged: () => { });

        Assert.IsEmpty(vm.Children);
    }

    [TestMethod]
    public void Reload_DiscardsStagedEditsEvenWhenChildrenWereAlreadyLoaded()
    {
        var vm = Vm(Group(TextField("child", "fallback")));

        vm.Children[0].Value = "edited";
        vm.Reload();

        Assert.AreEqual("fallback", vm.Children[0].Value, "Reload must still rebuild loaded children");
    }

    [TestMethod]
    public void Reload_LeavesANeverLoadedTreeLoadable()
    {
        // Reload is called per field whenever a config tab closes. A tree nobody opened has nothing to
        // discard, and building it there would put the per-plugin cost back into the selection path the
        // laziness exists to keep cheap -- but it must still be correct when later opened.
        var vm = Vm(Group(TextField("child", "value")));

        vm.Reload();

        Assert.HasCount(1, vm.Children);
        Assert.AreEqual("value", vm.Children[0].Value);
    }
}
