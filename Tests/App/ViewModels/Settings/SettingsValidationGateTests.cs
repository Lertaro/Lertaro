using System.IO;
using Lertaro.App.ViewModels.Settings;
using Lertaro.App.ViewModels.Settings.General;
using Lertaro.App.ViewModels.Settings.Plugins;
using Lertaro.Core;
using Lertaro.PluginSdk.Abstractions;

namespace Lertaro.App.Tests.ViewModels.Settings;

// Apply used to save whatever the pages had staged, including a value a page was visibly reporting as
// broken: WPF's Validation.Error only fires for rules expressed in a binding, so the rules a page works out
// for itself (a prefix or trigger keyword starting with a character the search syntax consumes) never
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
    public void GeneralPage_CarriedOverLegacyPrefix_WarnsWithoutRefusingToSave()
    {
        // What an upgraded install actually has on disk: the previous release's ':' default, which this
        // release reads as the exclusion operator. The field must still report it, but the window must stay
        // able to save every OTHER setting -- blocking there is a lockout over a value the user never typed
        // in this session (see QueryTokenPrefixRules.BlocksSaving).
        var settings = new UserSettings { GlobalTokenPrefix = Reserved };
        var vm = new GeneralSettingsViewModel(settings);

        Assert.IsNotNull(vm.PrefixError, "the field must keep saying the value cannot work");
        Assert.IsTrue(vm.HasPrefixError);
        Assert.IsEmpty(vm.ValidationErrors.ToList());
    }

    [TestMethod]
    public void GeneralPage_ClearedPrefix_DoesNotRefuseToSave()
    {
        // An emptied field is not a broken value: SettingsApplyHelpers maps blank to the default prefix.
        var vm = new GeneralSettingsViewModel(new UserSettings());
        vm.GlobalTokenPrefix = string.Empty;

        Assert.IsEmpty(vm.ValidationErrors.ToList());
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
            var field = PluginWithKeywordField();
            // Staged, i.e. what the user typed into the row: only then does an unusable value block a save
            // (a schema default or a carried-over value is warned about instead).
            field.ConfigFields[0].Value = Reserved + "audio";
            vm.Plugins.Plugins.Clear();
            vm.Plugins.Plugins.Add(field);

            var errors = vm.ValidationErrors.ToList();

            Assert.IsTrue(errors.Any(e => e.StartsWith("test_plugin.keyword: ", StringComparison.Ordinal)),
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
    public void Apply_Refused_SaysSoInTheWindowsStatusBar()
    {
        // A refused Apply used to be invisible: the button stays enabled by design and the page-level
        // warning can sit on a tab the user is not looking at, so clicking OK looked like nothing happening.
        var vm = new SettingsViewModel();
        try
        {
            vm.IsServiceReady = true;
            Assert.IsFalse(vm.Validation.HasRefusal, "nothing has been refused yet");

            vm.General.GlobalTokenPrefix = Reserved;
            Assert.IsFalse(vm.Apply());

            Assert.IsTrue(vm.Validation.HasRefusal);
            Assert.IsNotNull(vm.Validation.RefusalMessage);
        }
        finally
        {
            vm.Cleanup();
        }
    }

    [TestMethod]
    public void Gate_NoPluginPage_UsesTheGeneralPagesErrorsOnly()
    {
        // The gate is handed a Func precisely so an unbuilt Plugins page can stay unbuilt; a null page
        // contributes nothing rather than an error or an exception.
        var general = new GeneralSettingsViewModel(new UserSettings());
        general.GlobalTokenPrefix = Reserved;
        var gate = new SettingsValidationGate(general, () => null);

        var errors = gate.Errors.ToList();

        Assert.HasCount(1, errors);
        Assert.AreEqual(general.PrefixError, errors[0]);
    }

    [TestMethod]
    public void Gate_Refusal_IsShownUntilCleared()
    {
        var gate = new SettingsValidationGate(new GeneralSettingsViewModel(new UserSettings()), () => null);
        var notified = new List<string?>();
        gate.PropertyChanged += (_, e) => notified.Add(e.PropertyName);

        gate.Refuse(2);
        Assert.IsTrue(gate.HasRefusal);
        Assert.IsNotNull(gate.RefusalMessage);
        Assert.Contains(nameof(SettingsValidationGate.HasRefusal), notified);

        gate.Clear();
        Assert.IsFalse(gate.HasRefusal);
        Assert.IsNull(gate.RefusalMessage);
    }

    [TestMethod]
    public void UnvisitedPluginsPage_IsNotAskedForItsErrors()
    {
        // Asking the unvisited page for its errors would construct it, and its constructor is the one
        // expensive thing in the window (PluginLoaderHelper.BuildPluginList does genuine reflection over
        // every loaded plugin). An unvisited page holds no staged edit, so it can report no error either
        // way -- the same rule, and the same reason, as the other deferred sub-VMs. Two halves: the window
        // must hand the gate the BACKING FIELD (the Plugins property would build the page), and the gate
        // must skip a null page.
        var vm = Source("App/ViewModels/Settings/SettingsViewModel.cs");
        Assert.Contains("() => _plugins", vm,
            "the gate must be wired to the backing field, not to the lazy Plugins property");

        var gate = Source("App/ViewModels/Settings/SettingsValidationGate.cs");
        var errors = Between(gate, "internal IReadOnlyList<string> Errors", "\n    }");

        Assert.Contains("plugins()", errors, "the page must be asked through the injected lookup");
        Assert.Contains("!= null", errors, "the unvisited page must be skipped, not asked");
        Assert.DoesNotContain("Plugins.ValidationErrors", errors,
            "asking through the lazy property would build the page just to be told it is clean");
    }

    // The field whose rule is invisible to WPF's Validation.Error, so it used to slip through Apply: an
    // instant-answer trigger keyword starting with a character the search syntax consumes.
    private static PluginInfoViewModel PluginWithKeywordField()
    {
        var field = new PluginConfigFieldViewModel(
            "test_plugin",
            new PluginConfigField
            {
                Key = "keyword",
                FieldType = ConfigFieldType.Text,
                DefaultValue = Reserved + "audio",
                Validation = ConfigFieldValidation.TriggerKeyword,
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
