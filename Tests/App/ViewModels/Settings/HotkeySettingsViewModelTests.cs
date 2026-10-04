using Lertaro.App.ViewModels.Settings;
using Lertaro.Core;

namespace Lertaro.App.Tests.ViewModels.Settings;

// The save-time probe of the four global hook hotkeys: a combination the OS refuses (the system or
// another program registered it) goes back to its default and is reported, instead of saving into the
// half-broken state where the hook only sometimes wins the keystroke.
[TestClass]
public sealed class HotkeySettingsViewModelTests
{
    private static HotkeySettingsViewModel CreateViewModel(Func<uint, uint, bool>? combinationTaken)
    {
        var vm = new HotkeySettingsViewModel(new UserSettings(), new BlacklistSettingsViewModel(new UserSettings()), combinationTaken);
        // The other three default to real combinations, which an always-taken stub would report;
        // emptied here so each test answers for exactly the property it sets.
        vm.QuickSwitchComboHotkey = string.Empty;
        vm.QuickPanelHotkey = string.Empty;
        return vm;
    }

    [TestMethod]
    public void ATakenCombination_IsResetToItsDefaultAndReported()
    {
        var vm = CreateViewModel((_, _) => true);
        vm.QuickPanelHotkey = "Alt+Space";

        var taken = vm.ProbeGlobalCombinations();

        CollectionAssert.Contains(taken, "Alt+Space");
        Assert.AreEqual("Ctrl+F2", vm.QuickPanelHotkey);
    }

    [TestMethod]
    public void AFreeCombination_IsKept()
    {
        var vm = CreateViewModel((_, _) => false);
        vm.ToggleHotkeyValue = "Alt+Space";

        var taken = vm.ProbeGlobalCombinations();

        Assert.AreEqual(0, taken.Count);
        Assert.AreEqual("Alt+Space", vm.ToggleHotkeyValue);
    }

    // The double-tap form registers nothing with the OS: there is no key for another program's
    // registration to sit on, so it is never probed and never reset, whatever the probe would say.
    [TestMethod]
    public void ABareModifierToggle_IsNeverProbed()
    {
        var vm = CreateViewModel((_, _) => true);
        vm.ToggleHotkeyValue = "Ctrl";

        var taken = vm.ProbeGlobalCombinations();

        Assert.AreEqual(0, taken.Count);
        Assert.AreEqual("Ctrl", vm.ToggleHotkeyValue);
    }

    // The recorder does not produce one, but a hand-edited settings file can: it is saved as it
    // arrived, the same way it always was, rather than silently replaced with the default.
    [TestMethod]
    public void AnUnparsableValue_IsLeftAlone()
    {
        var vm = CreateViewModel((_, _) => true);
        vm.QuickSwitchComboHotkey = "Not+AKey";

        var taken = vm.ProbeGlobalCombinations();

        Assert.AreEqual(0, taken.Count);
        Assert.AreEqual("Not+AKey", vm.QuickSwitchComboHotkey);
    }

    // Quick Navigation's default is the empty string: nothing to probe, nothing to reset.
    [TestMethod]
    public void AnUnsetCombination_IsNeverProbed()
    {
        var vm = CreateViewModel((_, _) => true);
        vm.QuickNavigationHotkey = string.Empty;

        var taken = vm.ProbeGlobalCombinations();

        Assert.AreEqual(0, taken.Count);
        Assert.AreEqual(string.Empty, vm.QuickNavigationHotkey);
    }
}
