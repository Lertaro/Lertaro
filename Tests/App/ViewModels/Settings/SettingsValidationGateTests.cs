using System.IO;
using Lertaro.App.ViewModels.Settings;
using Lertaro.App.ViewModels.Settings.General;
using Lertaro.App.ViewModels.Settings.Plugins;
using Lertaro.Core;
using Lertaro.PluginSdk.Abstractions;

namespace Lertaro.App.Tests.ViewModels.Settings;

// Apply used to save whatever the pages had staged, including a value a page was visibly reporting as
// broken: WPF's Validation.Error only fires for rules expressed in a binding, so the rules a page works out
// for itself (a trigger character the search syntax consumes, two plugins claiming the same prefix) never
// reached it. These pin the two halves of the fix -- the pages report those errors, and Apply refuses
// while any of them is showing.
[TestClass]
public sealed class SettingsValidationGateTests
{
    // ':' is read by the search syntax as the exclusion operator, so a trigger claiming it never fires.
    private const string Reserved = ":";

    [TestMethod]
    public void GeneralPage_DefaultSettings_ReportsNothing()
    {
        var vm = new GeneralSettingsViewModel(new UserSettings());

        Assert.IsEmpty(vm.ValidationErrors.ToList());
    }

    [TestMethod]
    public void GeneralPage_UnusableGlobalPrefix_IsReported()
    {
        var vm = new GeneralSettingsViewModel(new UserSettings());
        vm.GlobalTokenPrefix = Reserved;

        var errors = vm.ValidationErrors.ToList();

        Assert.IsTrue(vm.HasPrefixError, "the page must be showing this itself, not only reporting it");
        Assert.AreEqual(vm.PrefixError, errors[0], "the app-wide prefix error must lead");
    }

    [TestMethod]
    public void GeneralPage_ResultTypeTriggerCollision_NamesTheRow()
    {
        var vm = new GeneralSettingsViewModel(new UserSettings());
        var row = vm.ResultTypeOrder.Items[0];
        row.TriggerChar = Reserved;

        var errors = vm.ValidationErrors.ToList();

        Assert.IsTrue(row.HasError, "the row must be showing this itself, not only reporting it");
        Assert.HasCount(1, errors);
        StringAssert.StartsWith(errors[0], row.DisplayName + ": ",
            "a bare message would not say which result type is at fault");
    }

    [TestMethod]
    public void SettingsWindow_CollectsTheErrorsOfEveryBuiltPage()
    {
        // The plugin config field is the page whose rules are invisible to WPF's Validation.Error, so it
        // is the one that used to slip through Apply.
        var vm = new SettingsViewModel();
        try
        {
            vm.General.GlobalTokenPrefix = Reserved;
            vm.Plugins.Plugins.Clear();
            vm.Plugins.Plugins.Add(PluginWithTriggerField());

            var errors = vm.ValidationErrors.ToList();

            Assert.IsTrue(errors.Any(e => e.StartsWith("test_plugin.prefix: ", StringComparison.Ordinal)),
                "a plugin config field's own error must reach the Settings window");
            Assert.IsTrue(errors.Any(e => e == vm.General.PrefixError),
                "so must the General page's");
        }
        finally
        {
            vm.Cleanup();
        }
    }

    [TestMethod]
    public void Apply_RefusesWhileAPageReportsAnError()
    {
        var vm = new SettingsViewModel();
        try
        {
            vm.IsServiceReady = true;
            vm.General.GlobalTokenPrefix = Reserved;
            Assert.IsNotEmpty(vm.ValidationErrors.ToList(), "the General page is reporting this, so Apply has something to refuse on");
            Assert.IsTrue(vm.CanApply, "the button stays enabled -- the walk is deliberately not on that path");
            Assert.IsFalse(vm.Apply(), "Apply must not save a value a page is reporting as broken");
        }
        finally
        {
            vm.Cleanup();
        }
    }

    [TestMethod]
    public void UnvisitedPluginsPage_IsNotAskedForItsErrors()
    {
        // Asking the unvisited page for its errors would construct it, and its constructor is the one
        // expensive thing in the window (PluginLoaderHelper.BuildPluginList does genuine reflection over
        // every loaded plugin). An unvisited page holds no staged edit, so it can report no error either
        // way -- the same rule, and the same reason, as the other deferred sub-VMs.
        var vm = Source("App/ViewModels/Settings/SettingsViewModel.cs");
        var collect = Between(vm, "private IEnumerable<string> CollectValidationErrors()", "\n    }");

        Assert.Contains("_plugins == null", collect, "the unvisited page must be skipped, not asked");
        Assert.DoesNotContain("Plugins.ValidationErrors", collect,
            "asking through the lazy property would build the page just to be told it is clean");
    }

    private static PluginInfoViewModel PluginWithTriggerField()
    {
        var field = new PluginConfigFieldViewModel(
            "test_plugin",
            new PluginConfigField
            {
                Key = "prefix",
                FieldType = ConfigFieldType.Text,
                MaxLength = 1,
                DefaultValue = Reserved,
            },
            new UserSettings(),
            null);

        return new PluginInfoViewModel(
            name: "TestPlugin",
            version: "1.0.0",
            dllFileName: "TestPlugin.dll",
            sdkVersion: "1.5.0",
            components: [],
            configFields: [field],
            description: "Test plugin description");
    }

    private static string Between(string source, string from, string to)
    {
        var start = source.IndexOf(from, StringComparison.Ordinal);
        Assert.IsGreaterThan(-1, start, $"could not find '{from}'");
        var end = source.IndexOf(to, start + from.Length, StringComparison.Ordinal);
        Assert.IsGreaterThan(-1, end, $"could not find '{to}' after '{from}'");
        return source.Substring(start, end - start);
    }

    private static string Source(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AGENTS.md")))
            dir = dir.Parent;
        Assert.IsNotNull(dir, "could not locate the repository root");
        var path = Path.Combine(dir!.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.IsTrue(File.Exists(path), $"expected a file at {path}");
        return File.ReadAllText(path);
    }
}
