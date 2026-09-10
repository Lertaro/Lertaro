using Lertaro.App.ViewModels.Settings.General;
using Lertaro.Core;

namespace Lertaro.App.Tests.ViewModels.Settings.General;

// Extracted out of GeneralSettingsViewModel (which had crossed the repo's per-file line limit) purely
// as a structural move, so these pin that the staging/persist contract survived the move.
[TestClass]
public sealed class DefaultFileManagerSettingsViewModelTests
{
    [TestMethod]
    public void Constructor_LoadsFromSettings()
    {
        var settings = new UserSettings();
        settings.DefaultFileManager.Enabled = true;
        settings.DefaultFileManager.Path = @"C:\Tools\fm.exe";
        settings.DefaultFileManager.Parameter = "/open:%s";

        var vm = new DefaultFileManagerSettingsViewModel(settings);

        Assert.IsTrue(vm.Enabled);
        Assert.AreEqual(@"C:\Tools\fm.exe", vm.Path);
        Assert.AreEqual("/open:%s", vm.Parameter);
    }

    [TestMethod]
    public void Edits_AreStagedUntilSave()
    {
        var settings = new UserSettings();
        var vm = new DefaultFileManagerSettingsViewModel(settings) { Enabled = true, Path = "x.exe", Parameter = "%s" };

        // Same staged-until-Apply contract the rest of the General page follows.
        Assert.IsFalse(settings.DefaultFileManager.Enabled);
        Assert.AreEqual(string.Empty, settings.DefaultFileManager.Path);

        vm.Save();

        Assert.IsTrue(settings.DefaultFileManager.Enabled);
        Assert.AreEqual("x.exe", settings.DefaultFileManager.Path);
        Assert.AreEqual("%s", settings.DefaultFileManager.Parameter);
    }
}
