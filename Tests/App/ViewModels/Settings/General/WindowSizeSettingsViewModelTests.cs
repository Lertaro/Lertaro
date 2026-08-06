using Lertaro.Core;
using Lertaro.App.Services;
using Lertaro.App.ViewModels.Settings.General;

namespace Lertaro.App.Tests.ViewModels.Settings.General;

[TestClass]
public sealed class MainWindowSettingsViewModelTests
{
    [TestMethod]
    public void Constructor_LoadsWidthAndHeightFromSettings()
    {
        var settings = new UserSettings();
        settings.MainWindow.Width = 900;
        settings.MainWindow.Height = 600;

        var vm = new MainWindowSettingsViewModel(settings);

        Assert.AreEqual(900, vm.Width);
        Assert.AreEqual(600, vm.Height);
    }

    [TestMethod]
    public void Width_WithinRange_SetsValue()
    {
        var vm = new MainWindowSettingsViewModel(new UserSettings()) { Width = UiMetrics.MinMainWindowWidth + 1 };

        Assert.AreEqual(UiMetrics.MinMainWindowWidth + 1, vm.Width);
    }

    [TestMethod]
    public void Width_BelowMinimum_Throws() =>
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new MainWindowSettingsViewModel(new UserSettings()).Width = UiMetrics.MinMainWindowWidth - 1);

    [TestMethod]
    public void Width_AboveMaximum_Throws() =>
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new MainWindowSettingsViewModel(new UserSettings()).Width = UiMetrics.MaxMainWindowWidth + 1);

    [TestMethod]
    public void Height_BelowMinimum_Throws() =>
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new MainWindowSettingsViewModel(new UserSettings()).Height = UiMetrics.MinMainWindowHeight - 1);

    [TestMethod]
    public void ResetCommand_Execute_RestoresDefaultWidthAndHeight()
    {
        var vm = new MainWindowSettingsViewModel(new UserSettings()) { Width = UiMetrics.MinMainWindowWidth, Height = UiMetrics.MinMainWindowHeight };

        vm.ResetCommand.Execute(null);

        Assert.AreEqual(UiMetrics.DefaultMainWindowWidth, vm.Width);
        Assert.AreEqual(UiMetrics.DefaultMainWindowHeight, vm.Height);
    }

    [TestMethod]
    public void Save_WritesStagedValuesToUserSettings()
    {
        var settings = new UserSettings();
        var vm = new MainWindowSettingsViewModel(settings) { Width = 950, Height = 650 };

        vm.Save();

        Assert.AreEqual(950, settings.MainWindow.Width);
        Assert.AreEqual(650, settings.MainWindow.Height);
    }
}

[TestClass]
public sealed class PreviewWindowSettingsViewModelTests
{
    [TestMethod]
    public void Constructor_LoadsWidthAndHeightFromSettings()
    {
        var settings = new UserSettings();
        settings.PreviewWindow.Width = 500;
        settings.PreviewWindow.Height = 700;

        var vm = new PreviewWindowSettingsViewModel(settings);

        Assert.AreEqual(500, vm.Width);
        Assert.AreEqual(700, vm.Height);
    }

    [TestMethod]
    public void Width_OutOfRange_Throws() =>
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new PreviewWindowSettingsViewModel(new UserSettings()).Width = UiMetrics.MaxPreviewWindowWidth + 1);

    [TestMethod]
    public void ResetCommand_Execute_RestoresDefaultWidthAndHeight()
    {
        var vm = new PreviewWindowSettingsViewModel(new UserSettings()) { Width = UiMetrics.MinPreviewWindowWidth };

        vm.ResetCommand.Execute(null);

        Assert.AreEqual(400, vm.Width);
        Assert.AreEqual(529, vm.Height);
    }

    [TestMethod]
    public void Save_WritesStagedValuesToUserSettings()
    {
        var settings = new UserSettings();
        var vm = new PreviewWindowSettingsViewModel(settings) { Width = 600, Height = 800 };

        vm.Save();

        Assert.AreEqual(600, settings.PreviewWindow.Width);
        Assert.AreEqual(800, settings.PreviewWindow.Height);
    }
}

[TestClass]
public sealed class SearchBarLayoutSettingsViewModelTests
{
    [TestMethod]
    public void Constructor_LoadsWidthHeightAndShowClockFromSettings()
    {
        var settings = new UserSettings();
        settings.SearchWindow.SearchBarWidth = 800;
        settings.SearchWindow.SearchBarHeight = 90;
        settings.SearchWindow.ShowClock = true;
        settings.SearchWindow.ReopenAsFullWindowOnRepeatHotkey = true;

        var vm = new SearchBarLayoutSettingsViewModel(settings);

        Assert.AreEqual(800, vm.SearchBarWidth);
        Assert.AreEqual(90, vm.SearchBarHeight);
        Assert.IsTrue(vm.ShowClock);
        Assert.IsTrue(vm.ReopenAsFullWindowOnRepeatHotkey);
    }

    [TestMethod]
    public void SearchBarWidth_BelowMinimum_Throws() =>
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new SearchBarLayoutSettingsViewModel(new UserSettings()).SearchBarWidth = 299);

    [TestMethod]
    public void SearchBarHeight_AboveMaximum_Throws() =>
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new SearchBarLayoutSettingsViewModel(new UserSettings()).SearchBarHeight = 121);

    [TestMethod]
    public void ResetCommand_Execute_RestoresDefaults()
    {
        var vm = new SearchBarLayoutSettingsViewModel(new UserSettings())
        {
            SearchBarWidth = 400,
            SearchBarHeight = 50,
            ShowClock = true,
            ReopenAsFullWindowOnRepeatHotkey = true,
        };

        vm.ResetCommand.Execute(null);

        Assert.AreEqual(570, vm.SearchBarWidth);
        Assert.AreEqual(60, vm.SearchBarHeight);
        Assert.IsFalse(vm.ShowClock);
        Assert.IsFalse(vm.ReopenAsFullWindowOnRepeatHotkey);
    }

    [TestMethod]
    public void Save_WritesStagedValuesToUserSettings()
    {
        var settings = new UserSettings();
        var vm = new SearchBarLayoutSettingsViewModel(settings) { SearchBarWidth = 700, ShowClock = true, ReopenAsFullWindowOnRepeatHotkey = true };

        vm.Save();

        Assert.AreEqual(700, settings.SearchWindow.SearchBarWidth);
        Assert.IsTrue(settings.SearchWindow.ShowClock);
        Assert.IsTrue(settings.SearchWindow.ReopenAsFullWindowOnRepeatHotkey);
    }

    [TestMethod]
    public void Save_AfterReset_ClearsRememberedWindowPosition()
    {
        var settings = new UserSettings();
        settings.SearchWindow.RelativeLeft = 0.3;
        settings.SearchWindow.RelativeTop = 0.4;
        var vm = new SearchBarLayoutSettingsViewModel(settings);

        vm.ResetCommand.Execute(null);
        vm.Save();

        Assert.IsNull(settings.SearchWindow.RelativeLeft);
        Assert.IsNull(settings.SearchWindow.RelativeTop);
    }

    [TestMethod]
    public void Save_WithoutReset_LeavesWindowPositionUntouched()
    {
        var settings = new UserSettings();
        settings.SearchWindow.RelativeLeft = 0.3;
        var vm = new SearchBarLayoutSettingsViewModel(settings);

        vm.Save();

        Assert.AreEqual(0.3, settings.SearchWindow.RelativeLeft);
    }
}
