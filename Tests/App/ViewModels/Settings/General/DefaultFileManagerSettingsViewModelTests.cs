using Lertaro.App.ViewModels.Settings.General;
using Lertaro.Core;

namespace Lertaro.App.Tests.ViewModels.Settings.General;

// Tests staging and persistence of the default file-manager settings.
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
