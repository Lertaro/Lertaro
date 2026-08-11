using Lertaro.App.ViewModels.Settings.LocalSend;
using Lertaro.Core;
using Lertaro.Core.Services.LocalSend.Models;

namespace Lertaro.App.Tests.ViewModels.Settings.LocalSend;

[TestClass]
public class LocalSendSettingsViewModelTests
{
    [TestMethod]
    public void LocalSendSettingsViewModel_ReadsAndWritesUserSettings()
    {
        var settings = new UserSettings();
        var vm = new LocalSendSettingsViewModel(settings);

        Assert.IsFalse(vm.Enabled);
        Assert.IsTrue(vm.CreateChecksums);
        Assert.IsTrue(vm.VerifyChecksums);
        vm.Enabled = true;
        vm.DeviceAlias = "Custom-PC";
        vm.DiscoveryTimeout = 3000;
        vm.QuickSave = true;
        vm.CreateChecksums = false;
        vm.VerifyChecksums = false;

        // Before Apply, UserSettings retains original values
        Assert.IsFalse(settings.LocalSend.Enabled);
        Assert.AreNotEqual("Custom-PC", settings.LocalSend.DeviceAlias);

        // After Apply, UserSettings updates to staged values
        vm.Apply();
        Assert.IsTrue(settings.LocalSend.Enabled);
        Assert.AreEqual("Custom-PC", settings.LocalSend.DeviceAlias);
        Assert.AreEqual(3000, settings.LocalSend.DiscoveryTimeout);
        Assert.IsTrue(settings.LocalSend.QuickSave);
        Assert.IsFalse(settings.LocalSend.CreateChecksums);
        Assert.IsFalse(settings.LocalSend.VerifyChecksums);

        // If DeviceAlias is empty on Apply, it auto-generates a random alias
        vm.DeviceAlias = "  ";
        vm.Apply();
        Assert.IsFalse(string.IsNullOrWhiteSpace(settings.LocalSend.DeviceAlias));
    }

    [TestMethod]
    public void LocalSendSettingsViewModel_DiscoveredDevices_UpdatesObservableCollection()
    {
        var settings = new UserSettings();
        var vm = new LocalSendSettingsViewModel(settings);

        Assert.IsEmpty(vm.DiscoveredDevices);

        var device = new LocalSendDeviceInfo
        {
            Alias = "Phone-1",
            IpAddress = "192.168.1.100"
        };

        vm.AddDiscoveredDevice(device);

        Assert.HasCount(1, vm.DiscoveredDevices);
        Assert.AreEqual("Phone-1", vm.DiscoveredDevices[0].Alias);
    }
}
