using Lertaro.App.Services;
using Lertaro.App.ViewModels.Settings.General;
using Lertaro.Core;

namespace Lertaro.App.Tests.ViewModels.Settings.General;

[TestClass]
public sealed class GeneralSettingsViewModelTests
{
    [TestMethod]
    public void EnableEverythingIpc_DefaultIsFalse()
    {
        var settings = new UserSettings();
        Assert.IsFalse(settings.EnableEverythingIpc);

        var vm = new GeneralSettingsViewModel(settings);
        Assert.IsFalse(vm.EnableEverythingIpc);
    }

    [TestMethod]
    public void EnableEverythingIpc_InitializesFromUserSettings()
    {
        var settings = new UserSettings { EnableEverythingIpc = true };
        var vm = new GeneralSettingsViewModel(settings);

        Assert.IsTrue(vm.EnableEverythingIpc);

        settings.EnableEverythingIpc = false;
        var vm2 = new GeneralSettingsViewModel(settings);

        Assert.IsFalse(vm2.EnableEverythingIpc);
    }

    [TestMethod]
    public void EnableEverythingIpc_Set_RaisesPropertyChanged()
    {
        var settings = new UserSettings { EnableEverythingIpc = true };
        var vm = new GeneralSettingsViewModel(settings);
        var changedProp = string.Empty;
        vm.PropertyChanged += (_, e) => changedProp = e.PropertyName ?? string.Empty;

        vm.EnableEverythingIpc = false;

        Assert.AreEqual(nameof(vm.EnableEverythingIpc), changedProp);
        Assert.IsFalse(vm.EnableEverythingIpc);
    }

    [TestMethod]
    public void ShowOpenedFoldersInInlineSearch_DefaultsToEnabledAndCanBeStaged()
    {
        var settings = new UserSettings();
        var vm = new GeneralSettingsViewModel(settings);

        Assert.IsTrue(settings.ShowOpenedFoldersInInlineSearch);
        Assert.IsTrue(vm.ShowOpenedFoldersInInlineSearch);

        vm.ShowOpenedFoldersInInlineSearch = false;

        Assert.IsFalse(vm.ShowOpenedFoldersInInlineSearch);
        Assert.IsTrue(settings.ShowOpenedFoldersInInlineSearch);
    }

    [TestMethod]
    public void InlineSearchWindowWidthPercent_DefaultsToFortyAndTakesOnlyTheBoxsRange()
    {
        var settings = new UserSettings();
        var vm = new GeneralSettingsViewModel(settings);

        // Unset reads as the default number, and stays staged until the page applies.
        Assert.IsNull(settings.InlineSearchWindowWidthPercent);
        Assert.AreEqual(UiMetrics.DefaultInlineCardWidthPercent, vm.InlineSearchWindowWidthPercent);

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => vm.InlineSearchWindowWidthPercent = UiMetrics.MinInlineCardWidthPercent - 1);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => vm.InlineSearchWindowWidthPercent = UiMetrics.MaxInlineCardWidthPercent + 1);

        vm.InlineSearchWindowWidthPercent = 30;

        Assert.AreEqual(30, vm.InlineSearchWindowWidthPercent);
        Assert.IsNull(settings.InlineSearchWindowWidthPercent);
    }

    [TestMethod]
    public void InlineSearchWindowWidthPercent_DefaultIsStoredAsUnsetNotAsForty()
    {
        // Writing 40 back would read as an explicit choice and cost the card the screen floor it keeps when
        // nobody picked a number.
        Assert.IsNull(GeneralSettingsApplier.ToStoredInlineCardWidthPercent(UiMetrics.DefaultInlineCardWidthPercent));
        Assert.AreEqual(30, GeneralSettingsApplier.ToStoredInlineCardWidthPercent(30));
        Assert.AreEqual(UiMetrics.MaxInlineCardWidthPercent, GeneralSettingsApplier.ToStoredInlineCardWidthPercent(UiMetrics.MaxInlineCardWidthPercent));
    }
}
