using System.Collections.ObjectModel;
using Lertaro.App.Helpers;
using Lertaro.App.ViewModels.Settings.Plugins;

namespace Lertaro.App.Tests.ViewModels.Settings.Plugins;

[TestClass]
public sealed class PluginManagementViewModelSortTests
{
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
    public void SortForDisplay_FullyDisabledPlugins_SinkBelowAllActiveOnes()
    {
        var disabledConfigurable = MakePlugin("DisabledConfig", fullyDisabled: true, hasConfigFields: false, hasToggleable: true);
        var activePlain = MakePlugin("ActivePlain", hasToggleable: true);
        var readOnly = MakePlugin("ReadOnly", hasToggleable: false);
        var disabledPlain = MakePlugin("ADisabled", fullyDisabled: true, hasToggleable: true);

        var sorted = PluginLoaderHelper.SortForDisplay(new List<PluginInfoViewModel> { disabledConfigurable, activePlain, readOnly, disabledPlain });

        // Every active plugin precedes every fully-disabled one; rank and name order intact within each.
        Assert.AreEqual("ActivePlain", sorted[0].Name);
        Assert.AreEqual("ReadOnly", sorted[1].Name);
        Assert.AreEqual("ADisabled", sorted[2].Name);
        Assert.AreEqual("DisabledConfig", sorted[3].Name);
    }

    [TestMethod]
    public void SortForDisplay_NoDisabledPlugins_KeepsRankAndNameOrder()
    {
        var plain = MakePlugin("BPlain", hasToggleable: true);
        var configurable = MakePlugin("AConfig", hasConfigFields: true);

        var sorted = PluginLoaderHelper.SortForDisplay(new List<PluginInfoViewModel> { plain, configurable });

        // Configurable plugins are still the most actionable band, alphabetical inside it.
        Assert.AreEqual("AConfig", sorted[0].Name);
        Assert.AreEqual("BPlain", sorted[1].Name);
    }

    [TestMethod]
    public void MovePluginForDisabledState_LastToggleTurnedOff_MovesIntoSortedDisabledTail()
    {
        var active = MakePlugin("Active", hasToggleable: true);
        var other = MakePlugin("Beta", fullyDisabled: true, hasToggleable: true);
        var moving = MakePlugin("Moving", hasToggleable: true);
        var plugins = new ObservableCollection<PluginInfoViewModel> { moving, active, other };

        // Turning the moving plugin's last component off crosses the fully-disabled boundary;
        // it must land after "Beta" (alphabetical inside the disabled tail), not bluntly last.
        moving.RawComponents.Single().IsEnabled = false;
        PluginManagementViewModel.MovePluginForDisabledState(plugins, moving);

        CollectionAssert.AreEquivalent(new[] { "Active", "Beta", "Moving" }, plugins.Select(p => p.Name).ToList());
        Assert.AreEqual(2, plugins.IndexOf(moving));
    }

    [TestMethod]
    public void MovePluginForDisabledState_Reenabled_ReturnsToAlphabeticalActivePosition()
    {
        var active = MakePlugin("Active", hasToggleable: true);
        var disabled = MakePlugin("Zed", fullyDisabled: true, hasToggleable: true);
        var reenabling = MakePlugin("Mid", fullyDisabled: true, hasToggleable: true);
        var plugins = new ObservableCollection<PluginInfoViewModel> { active, disabled, reenabling };

        // Reactivating inserts before the first still-disabled plugin ("Zed"), not bluntly last.
        reenabling.RawComponents.Single().IsEnabled = true;
        PluginManagementViewModel.MovePluginForDisabledState(plugins, reenabling);

        CollectionAssert.AreEquivalent(new[] { "Active", "Mid", "Zed" }, plugins.Select(p => p.Name).ToList());
        Assert.AreEqual(1, plugins.IndexOf(reenabling));
    }

    [TestMethod]
    public void MovePluginForDisabledState_PluginNotInList_NoOperation()
    {
        var plugins = new ObservableCollection<PluginInfoViewModel> { MakePlugin("Active", hasToggleable: true) };
        var stranger = MakePlugin("Stranger", hasToggleable: true);

        PluginManagementViewModel.MovePluginForDisabledState(plugins, stranger);

        Assert.HasCount(1, plugins);
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
