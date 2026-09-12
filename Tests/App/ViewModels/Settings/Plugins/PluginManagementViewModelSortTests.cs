using System.Collections.ObjectModel;
using Lertaro.App.Helpers;
using Lertaro.App.ViewModels.Settings.Plugins;

namespace Lertaro.App.Tests.ViewModels.Settings.Plugins;

[TestClass]
public sealed class PluginManagementViewModelSortTests
{
    // Rank bands (configurable > switchable > inert) and disabled state, without a live plugin dir.
    private static PluginInfoViewModel MakePlugin(
        string name,
        bool fullyDisabled = false,
        bool hasConfigFields = false,
        bool hasToggleable = true)
    {
        var components = new List<PluginComponentViewModel>();
        if (hasConfigFields)
        {
            var field = new PluginConfigFieldViewModel(
                name, new PluginSdk.Abstractions.PluginConfigField { Key = "k", FieldType = PluginSdk.Abstractions.ConfigFieldType.Text, DefaultValue = "" },
                new Core.UserSettings(), () => { });
            return new PluginInfoViewModel(name, "1.0", name + ".dll", "1.0-sdk", components, [field]);
        }

        if (hasToggleable)
        {
            components.Add(new PluginComponentViewModel(name + "::c", PluginComponentType.Action, name, isEnabled: !fullyDisabled));
        }
        else
        {
            // Translation/theme-only plugins cannot be disabled and never count as fully disabled.
            components.Add(new PluginComponentViewModel(name + "::t", PluginComponentType.TranslationProvider, name, isEnabled: true));
        }

        return new PluginInfoViewModel(name, "1.0", name + ".dll", "1.0-sdk", components, []);
    }

    [TestMethod]
    public void SortForDisplay_DefaultOrder_IsRankThenName()
    {
        var inert = MakePlugin("Inert", hasToggleable: false);
        var plain = MakePlugin("Plain");
        var configurable = MakePlugin("Config", hasConfigFields: true);
        var disabled = MakePlugin("Disabled", fullyDisabled: true);

        var sorted = PluginLoaderHelper.SortForDisplay(new List<PluginInfoViewModel> { inert, disabled, plain, configurable });

        // Disabled state does NOT sink a plugin in the default order: configurable first, then
        // switchable, then inert -- alphabetical within each band.
        CollectionAssert.AreEqual(new[] { "Config", "Disabled", "Plain", "Inert" }, sorted.Select(p => p.Name).ToList());
    }

    [TestMethod]
    public void SortPluginsList_Default_IsRankThenName()
    {
        var plugins = new List<PluginInfoViewModel>
        {
            MakePlugin("Zed", hasToggleable: false),
            MakePlugin("Alpha"),
            MakePlugin("Mid", fullyDisabled: true),
            MakePlugin("ACfg", hasConfigFields: true),
        };

        var sorted = PluginManagementViewModel.SortPluginsList(plugins, disabledLast: false);

        CollectionAssert.AreEqual(new[] { "ACfg", "Alpha", "Mid", "Zed" }, sorted.Select(p => p.Name).ToList());
    }

    [TestMethod]
    public void SortPluginsList_DisabledLast_SinksDisabledBelowActive()
    {
        var plugins = new List<PluginInfoViewModel>
        {
            MakePlugin("Zed", hasToggleable: false),
            MakePlugin("DisabledB", fullyDisabled: true),
            MakePlugin("Alpha"),
            MakePlugin("DisabledA", fullyDisabled: true),
        };

        var sorted = PluginManagementViewModel.SortPluginsList(plugins, disabledLast: true);

        // Active first (rank then name), then the disabled tail (rank then name).
        CollectionAssert.AreEqual(
            new[] { "Alpha", "Zed", "DisabledA", "DisabledB" }, sorted.Select(p => p.Name).ToList());
    }

    [TestMethod]
    public void SyncRuntimeStatusCollection_UnchangedOrder_DoesNotRaiseCollectionChanges()
    {
        var first = new PluginRuntimeStatusItemViewModel(MakePlugin("First"));
        var second = new PluginRuntimeStatusItemViewModel(MakePlugin("Second"));
        var statuses = new ObservableCollection<PluginRuntimeStatusItemViewModel> { first, second };
        var changeCount = 0;
        statuses.CollectionChanged += (_, _) => changeCount++;

        PluginManagementViewModel.SyncRuntimeStatusCollection(statuses, [first, second]);

        Assert.AreEqual(0, changeCount);
    }

    [TestMethod]
    public void SyncRuntimeStatusCollection_NewOrder_MovesExistingRowsAndRemovesMissingRows()
    {
        var first = new PluginRuntimeStatusItemViewModel(MakePlugin("First"));
        var second = new PluginRuntimeStatusItemViewModel(MakePlugin("Second"));
        var replacement = new PluginRuntimeStatusItemViewModel(MakePlugin("Replacement"));
        var statuses = new ObservableCollection<PluginRuntimeStatusItemViewModel> { first, second };

        PluginManagementViewModel.SyncRuntimeStatusCollection(statuses, [second, replacement]);

        Assert.HasCount(2, statuses);
        Assert.AreSame(second, statuses[0]);
        Assert.AreSame(replacement, statuses[1]);
    }
}
