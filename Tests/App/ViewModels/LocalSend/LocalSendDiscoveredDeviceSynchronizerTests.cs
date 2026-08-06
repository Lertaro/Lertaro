using System.Collections.ObjectModel;
using Lertaro.App.ViewModels.LocalSend;
using Lertaro.Core.Services.LocalSend.Models;

namespace Lertaro.App.Tests.ViewModels.LocalSend;

[TestClass]
public sealed class LocalSendDiscoveredDeviceSynchronizerTests
{
    [TestMethod]
    public void Synchronize_KeepsDevicesWithDifferentPortsOrFingerprints()
    {
        var items = new ObservableCollection<LocalSendSendDeviceItem>();
        var devices = new[]
        {
            new LocalSendDeviceInfo { Alias = "Port", IpAddress = "192.168.1.20", Port = 53317, Fingerprint = "one" },
            new LocalSendDeviceInfo { Alias = "Fingerprint", IpAddress = "192.168.1.20", Port = 53317, Fingerprint = "two" },
            new LocalSendDeviceInfo { Alias = "Other port", IpAddress = "192.168.1.20", Port = 54444, Fingerprint = "one" }
        };

        LocalSendDiscoveredDeviceSynchronizer.Synchronize(items, devices);

        Assert.HasCount(3, items);
    }
}
