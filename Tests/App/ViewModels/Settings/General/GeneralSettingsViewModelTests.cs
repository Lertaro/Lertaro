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
    public void InlineSearchWindowWidthPercent_DefaultsToSixtyAndTakesOnlyTheBoxsRange()
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
    public void InlineSearchWindowWidthPercent_DefaultIsStoredAsUnsetNotAsSixty()
    {
        // Writing 60 back would read as an explicit choice and cost the card the screen floor it keeps when
        // nobody picked a number.
        Assert.IsNull(GeneralSettingsApplier.ToStoredInlineCardWidthPercent(UiMetrics.DefaultInlineCardWidthPercent));
        Assert.AreEqual(30, GeneralSettingsApplier.ToStoredInlineCardWidthPercent(30));
        Assert.AreEqual(UiMetrics.MaxInlineCardWidthPercent, GeneralSettingsApplier.ToStoredInlineCardWidthPercent(UiMetrics.MaxInlineCardWidthPercent));
    }

    [TestMethod]
    public void InlineSearchWindowMaxRows_DefaultsToTenAndTakesOnlyTheBoxsRange()
    {
        var settings = new UserSettings();
        var vm = new GeneralSettingsViewModel(settings);

        Assert.AreEqual(UiMetrics.DefaultInlineCardRows, vm.InlineSearchWindowMaxRows);

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => vm.InlineSearchWindowMaxRows = UiMetrics.MinInlineCardRows - 1);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => vm.InlineSearchWindowMaxRows = UiMetrics.MaxInlineCardRows + 1);

        vm.InlineSearchWindowMaxRows = 6;

        // Staged per the page's rule: the box shows 6, the settings keep the default until Apply() runs.
        Assert.AreEqual(6, vm.InlineSearchWindowMaxRows);
        Assert.AreEqual(UiMetrics.DefaultInlineCardRows, settings.InlineSearchWindowMaxRows);
    }

    [TestMethod]
    public void InlineSearchDialogMaxRows_DefaultsToFourAndTakesTheSameRangeAsTheWindowBox()
    {
        var settings = new UserSettings();
        var vm = new GeneralSettingsViewModel(settings);

        // The dialog starts where the card used to be capped over one, and the two boxes are independent.
        Assert.AreEqual(UiMetrics.DefaultInlineCardDialogRows, vm.InlineSearchDialogMaxRows);

        vm.InlineSearchDialogMaxRows = 8;
        Assert.AreEqual(8, vm.InlineSearchDialogMaxRows);
        Assert.AreEqual(UiMetrics.DefaultInlineCardRows, vm.InlineSearchWindowMaxRows, "the window box keeps its own count");

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => vm.InlineSearchDialogMaxRows = UiMetrics.MinInlineCardRows - 1);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => vm.InlineSearchDialogMaxRows = UiMetrics.MaxInlineCardRows + 1);
    }

    // Apply() is deliberately not called from these: it ends in UserSettings.Save(), which would overwrite
    // this machine's real configuration. GeneralSettingsApplier's assignment is covered by review.
    [TestMethod]
    public void Constructor_ReadsRankingDetailsExportSwitch_DefaultingToOn()
    {
        Assert.IsTrue(new GeneralSettingsViewModel(new UserSettings()).EnableRankingDetailsExport);
        Assert.IsFalse(new GeneralSettingsViewModel(new UserSettings { EnableRankingDetailsExport = false }).EnableRankingDetailsExport);
    }

    [TestMethod]
    public void RankingDetailsExportSwitch_SetterRoundTrips()
    {
        var vm = new GeneralSettingsViewModel(new UserSettings { EnableRankingDetailsExport = false });

        vm.EnableRankingDetailsExport = true;

        Assert.IsTrue(vm.EnableRankingDetailsExport);
    }
}
