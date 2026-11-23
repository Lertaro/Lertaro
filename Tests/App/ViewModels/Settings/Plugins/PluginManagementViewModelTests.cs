using Lertaro.App.ViewModels.Settings.Plugins;
using Lertaro.App.Services;
using Lertaro.Core;

namespace Lertaro.App.Tests.ViewModels.Settings.Plugins;

[TestClass]
[DoNotParallelize]
public sealed class PluginManagementViewModelTests
{
    private string _previousCulture = string.Empty;

    [TestInitialize]
    public void Initialize() => _previousCulture = TranslationManager.Instance.CurrentCulture;

    [TestCleanup]
    public void Cleanup() => TranslationManager.Instance.CurrentCulture = _previousCulture;

    [TestMethod]
    public void LanguageRefresh_NotifiesAndTranslatesBothSortLabels()
    {
        var translations = TranslationManager.Instance;
        translations.CurrentCulture = "zh-CN";
        translations.ReloadTranslations();
        var viewModel = new PluginManagementViewModel(new UserSettings());
        var labelsChanged = 0;
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(viewModel.PluginSortLabel)) labelsChanged++;
        };
        try
        {
            translations.CurrentCulture = "en-US";

            Assert.IsGreaterThan(0, labelsChanged);
            Assert.AreEqual("Enabled", viewModel.PluginSortLabel);
            viewModel.TogglePluginSort();
            Assert.AreEqual("Name", viewModel.PluginSortLabel);
            labelsChanged = 0;
            translations.CurrentCulture = "zh-CN";
            Assert.IsGreaterThan(0, labelsChanged);
            Assert.AreEqual("名称", viewModel.PluginSortLabel);
        }
        finally { viewModel.Cleanup(); }
    }

    [TestMethod]
    public void LanguageRefresh_PreservesIndividualFeatureChoices()
    {
        var first = new PluginComponentViewModel("P::Action::One", PluginComponentType.Action, "One", true);
        var second = new PluginComponentViewModel("P::Action::Two", PluginComponentType.Action, "Two", true);
        var edited = new PluginInfoViewModel("P", "1", "P.dll", "1", [first, second], []);
        first.IsEnabled = false;
        var replacement = new PluginInfoViewModel("P", "1", "P.dll", "1",
            [new(first.ComponentId, PluginComponentType.Action, "One", true), new(second.ComponentId, PluginComponentType.Action, "Two", true)], []);
        PluginManagementViewModel.RestoreEnablementEdits([edited], [replacement]);
        Assert.IsFalse(replacement.RawComponents[0].IsEnabled);
        Assert.IsTrue(replacement.RawComponents[1].IsEnabled);
        Assert.IsTrue(replacement.IsPluginEnabled);
    }

    [TestMethod]
    public void Save_AllFeaturesOff_BatchesTwentyFiveWholePlugins()
    {
        var plugins = Enumerable.Range(0, 25).Select(i => new PluginInfoViewModel("P", "1", $"p{i}.dll", "1",
            [new($"p{i}.dll::Action::One", PluginComponentType.Action, "One", true)], [])).ToList();
        foreach (var plugin in plugins) plugin.RawComponents[0].IsEnabled = false;
        var settings = new UserSettings();
        PluginManagementViewModel.SavePluginEnablement(settings, plugins);
        Assert.HasCount(25, settings.DisabledPluginAssemblies);
        Assert.IsTrue(plugins.All(p => !p.IsPluginEnabled));
        plugins[0].RawComponents[0].IsEnabled = true;
        PluginManagementViewModel.SavePluginEnablement(settings, plugins);
        Assert.HasCount(24, settings.DisabledPluginAssemblies);
        Assert.IsTrue(plugins[0].IsPluginEnabled);
    }

    [TestMethod]
    public void LanguageRefresh_PreservesPendingSwitchWithoutOverwritingUntouchedPluginState()
    {
        var edited = new PluginInfoViewModel("A", "1", "a.dll", "1", [], []);
        var untouched = new PluginInfoViewModel("B", "1", "B.dll", "1", [], []);
        edited.IsPluginEnabled = false;
        var replacement = new PluginInfoViewModel("Translated A", "1", "A.DLL", "1", [], []);
        var independentlyDisabled = new PluginInfoViewModel("Translated B", "1", "B.dll", "1", [], [], isPluginEnabled: false);

        PluginManagementViewModel.RestoreEnablementEdits([edited, untouched], [replacement, independentlyDisabled]);

        Assert.IsFalse(replacement.IsPluginEnabled);
        Assert.IsTrue(replacement.IsPluginEnablementDirty);
        Assert.IsFalse(independentlyDisabled.IsPluginEnabled);
        Assert.IsFalse(independentlyDisabled.IsPluginEnablementDirty);
    }

    [TestMethod]
    public void Save_MergesOnlyEditedPluginsAndPreservesUnloadedPluginConfiguration()
    {
        var settings = new UserSettings
        {
            DisabledPluginAssemblies = ["a.DLL", "absent.dll"],
            DisabledPluginComponents = ["A.dll::Action::One"]
        };
        settings.PluginSettings["A"] = new() { ["secret"] = "preserved" };
        var enabled = new PluginInfoViewModel("A", "1", "A.dll", "1", [], [], isPluginEnabled: false);
        var disabled = new PluginInfoViewModel("B", "1", "B.dll", "1", [], []);
        var untouched = new PluginInfoViewModel("Absent", "1", "absent.dll", "1", [], []);
        enabled.IsPluginEnabled = true;
        disabled.IsPluginEnabled = false;
        PluginManagementViewModel.SavePluginEnablement(settings, [enabled, disabled, untouched]);
        CollectionAssert.AreEquivalent(new[] { "absent.dll", "B.dll" }, settings.DisabledPluginAssemblies);
        Assert.AreEqual("preserved", settings.PluginSettings["A"]["secret"]);
        Assert.IsEmpty(settings.DisabledPluginComponents);
    }
}
