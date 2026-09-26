using System.IO;
using Lertaro.App.ViewModels.Settings.LocalSend;
using Lertaro.Core;
using Lertaro.Core.Services.LocalSend;
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

    [TestMethod]
    public void LocalSendSettingsViewModel_KeepsTheShellTokenAndShowsTheFolderItResolvesTo()
    {
        var settings = new UserSettings();
        var vm = new LocalSendSettingsViewModel(settings);

        // The row shows a real folder, but what the page stores stays the token -- applying the settings
        // page must not freeze today's Downloads location into the file.
        Assert.AreEqual(LocalSendSettingsModel.DefaultDownloadDirectory, vm.DownloadDirectory);
        Assert.IsTrue(Directory.Exists(vm.DownloadDirectoryDisplay));

        vm.Apply();
        Assert.AreEqual(LocalSendSettingsModel.DefaultDownloadDirectory, settings.LocalSend.DownloadDirectory);
    }

    [TestMethod]
    public void LocalSendSettingsViewModel_StoresThePortAndWeedsOutAnUnusableOne()
    {
        var settings = new UserSettings();
        var vm = new LocalSendSettingsViewModel(settings);

        vm.Port = 53400;
        vm.Apply();
        Assert.AreEqual(53400, settings.LocalSend.Port);

        // The row is a text box, so whatever is in it when Apply runs has to be judged there: a value the
        // sockets cannot bind, or 0, would otherwise reach a settings file whose readers all take 0 to mean
        // "not configured" and quietly bind the default instead.
        foreach (var unusable in new[] { 0, -1, 70000, 65536 })
        {
            vm.Port = unusable;
            vm.Apply();
            Assert.AreEqual(LocalSendDiscoveryService.DefaultPort, settings.LocalSend.Port, $"port {unusable}");
        }

        // Both ends of the legal range survive untouched.
        vm.Port = 1;
        vm.Apply();
        Assert.AreEqual(1, settings.LocalSend.Port);
    }
}
