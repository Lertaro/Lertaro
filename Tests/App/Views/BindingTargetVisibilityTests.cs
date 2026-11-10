using System.Reflection;
using Lertaro.App.ViewModels.Search;
using Lertaro.App.ViewModels.Settings;
using Lertaro.App.ViewModels.Settings.Plugins;

namespace Lertaro.App.Tests.Views;

// A WPF binding path is resolved with a PUBLIC-only reflection lookup: a non-public member never resolves --
// no exception, no log, the binding is simply skipped and every trigger and text it drives keeps its default.
// Several bindings shipped that way once (the result-area hints, the plugin trigger warnings, the settings
// status bar), each silently disabling the feature it wired up.
//
// This pins the members those bindings name. It cannot see a binding added later, so a NEW binding to a
// non-public member still has to be caught by reading the XAML -- but everything bound today is covered.
[TestClass]
public sealed class BindingTargetVisibilityTests
{
    [TestMethod]
    public void BoundMembers_ArePublic()
    {
        // SearchWindow.xaml (the result-area hints and the invalid-regex line).
        AssertPublicProperty(typeof(SearchViewModel), nameof(SearchViewModel.Hints));
        AssertPublicProperty(typeof(SearchViewHints), nameof(SearchViewHints.ShowWelcomeHint));
        AssertPublicProperty(typeof(SearchViewHints), nameof(SearchViewHints.ShowNoResultsHint));
        AssertPublicProperty(typeof(SearchViewHints), nameof(SearchViewHints.InvalidRegexHint));

        // Templates.xaml (the trigger-keyword warning).
        AssertPublicProperty(typeof(PluginConfigFieldViewModel), nameof(PluginConfigFieldViewModel.TriggerKeywordError));

        // SettingsWindow.xaml (the status bar's refusal reason).
        AssertPublicProperty(typeof(SettingsViewModel), nameof(SettingsViewModel.Validation));
        AssertPublicProperty(typeof(SettingsValidationGate), nameof(SettingsValidationGate.RefusalMessage));
        AssertPublicProperty(typeof(SettingsValidationGate), nameof(SettingsValidationGate.HasRefusal));
    }

    private static void AssertPublicProperty(Type type, string name)
    {
        var property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);

        Assert.IsNotNull(property,
            $"{type.Name}.{name} is named by a binding path, so it must be a public property -- a non-public one is skipped silently");
        Assert.IsTrue(property!.GetMethod!.IsPublic,
            $"{type.Name}.{name} must have a public getter for a binding to read it");
    }
}
