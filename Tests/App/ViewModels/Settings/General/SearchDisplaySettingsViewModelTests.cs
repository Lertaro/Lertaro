using Lertaro.App.ViewModels.Settings.General;
using Lertaro.Core;

namespace Lertaro.App.Tests.ViewModels.Settings.General;

[TestClass]
public sealed class SearchDisplaySettingsViewModelTests
{
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void Save_CommitsOnlyItsOwnWindowDisplayAndCanRestoreFollowingTheMouse(bool isQuickWindow)
    {
        var settings = new UserSettings();
        settings.SearchWindow.DefaultDisplay = @"\\.\DISPLAY1";
        settings.MainWindow.DefaultDisplay = @"\\.\DISPLAY3";
        var model = new SearchDisplaySettingsViewModel(settings, isQuickWindow) { DeviceName = @"\\.\DISPLAY2" };

        Assert.AreEqual(@"\\.\DISPLAY1", settings.SearchWindow.DefaultDisplay);
        Assert.AreEqual(@"\\.\DISPLAY3", settings.MainWindow.DefaultDisplay);
        model.Save();
        Assert.AreEqual(@"\\.\DISPLAY2", new SearchDisplaySettingsViewModel(settings, isQuickWindow).DeviceName);
        model.DeviceName = string.Empty;
        model.Save();
        Assert.AreEqual(string.Empty, isQuickWindow ? settings.SearchWindow.DefaultDisplay : settings.MainWindow.DefaultDisplay);
        Assert.AreEqual(isQuickWindow ? @"\\.\DISPLAY3" : @"\\.\DISPLAY1",
            isQuickWindow ? settings.MainWindow.DefaultDisplay : settings.SearchWindow.DefaultDisplay);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void DeviceName_TransientNullFromBinding_PreservesTheStagedSelection(bool isQuickWindow)
    {
        var model = new SearchDisplaySettingsViewModel(new UserSettings(), isQuickWindow) { DeviceName = @"\\.\DISPLAY2" };

        model.DeviceName = null!;

        Assert.AreEqual(@"\\.\DISPLAY2", model.DeviceName);
    }

    [TestMethod]
    public void WindowPages_SaveAndResetTheirDisplaysIndependently()
    {
        var settings = new UserSettings();
        var quick = new SearchBarLayoutSettingsViewModel(settings);
        var full = new MainWindowSettingsViewModel(settings);
        quick.Display.DeviceName = @"\\.\DISPLAY1";
        full.Display.DeviceName = @"\\.\DISPLAY2";

        quick.Display.Save();
        full.Display.Save();
        quick.ResetCommand.Execute(null);

        Assert.AreEqual(string.Empty, quick.Display.DeviceName);
        Assert.AreEqual(@"\\.\DISPLAY2", full.Display.DeviceName);
        Assert.AreEqual(@"\\.\DISPLAY1", settings.SearchWindow.DefaultDisplay);
        quick.Display.Save();
        Assert.AreEqual(string.Empty, settings.SearchWindow.DefaultDisplay);
        Assert.AreEqual(@"\\.\DISPLAY2", settings.MainWindow.DefaultDisplay);

        full.ResetCommand.Execute(null);
        full.Display.Save();
        Assert.AreEqual(string.Empty, settings.MainWindow.DefaultDisplay);
    }
}
