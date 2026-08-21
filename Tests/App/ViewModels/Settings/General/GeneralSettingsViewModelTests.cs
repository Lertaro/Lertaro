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
}
