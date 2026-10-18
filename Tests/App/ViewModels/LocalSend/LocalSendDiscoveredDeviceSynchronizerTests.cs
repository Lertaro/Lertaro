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
        // Three rows that a fingerprint-only key would collapse: two user sessions on one machine share an
        // address and differ only by port, and the send has to be able to pick which one pops the dialog.
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

    [TestMethod]
    public void Synchronize_KeepsTheRowAndItsPinWhenTheDeviceChangesAddress()
    {
        var items = new ObservableCollection<LocalSendSendDeviceItem>();
        LocalSendDiscoveredDeviceSynchronizer.Synchronize(items,
            [new LocalSendDeviceInfo { Alias = "Phone", IpAddress = "192.168.1.20", Port = 53317, Fingerprint = "peer" }]);
        var row = items.Single();
        row.Pin = "123456";

        LocalSendDiscoveredDeviceSynchronizer.Synchronize(items,
            [new LocalSendDeviceInfo { Alias = "Phone", IpAddress = "192.168.1.55", Port = 53317, Fingerprint = "peer" }]);

        Assert.HasCount(1, items);
        Assert.AreSame(row, items[0]);
        Assert.AreEqual("192.168.1.55:53317", items[0].EndpointText);
        Assert.AreEqual("123456", row.Pin);
    }
}
