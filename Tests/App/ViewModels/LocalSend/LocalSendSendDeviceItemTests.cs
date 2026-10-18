using Lertaro.App.ViewModels.LocalSend;
using Lertaro.Core.Services.LocalSend.Models;

namespace Lertaro.App.Tests.ViewModels.LocalSend;

[TestClass]
public sealed class LocalSendSendDeviceItemTests
{
    [TestMethod]
    public void UpdateDevice_RaisesUsesHttpsChangedWhenProtocolChanges()
    {
        var item = new LocalSendSendDeviceItem(new LocalSendDeviceInfo { Protocol = "http" });
        var changedProperties = new List<string?>();
        item.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName);

        item.UpdateDevice(new LocalSendDeviceInfo { Protocol = "https" });

        Assert.IsTrue(item.UsesHttps);
        CollectionAssert.Contains(changedProperties, nameof(LocalSendSendDeviceItem.UsesHttps));
    }

    [TestMethod]
    public void UpdateDevice_RaisesTheTagReadOffTheAddressWhenTheDeviceMoves()
    {
        var item = new LocalSendSendDeviceItem(new LocalSendDeviceInfo { Alias = "Phone", IpAddress = "192.168.1.20", Fingerprint = "peer", Port = 53317 });
        var changedProperties = new List<string?>();
        item.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName);

        item.UpdateDevice(new LocalSendDeviceInfo { Alias = "Phone", IpAddress = "192.168.1.55", Fingerprint = "peer", Port = 53317 });

        Assert.AreEqual("55", item.FingerprintTag);
        Assert.AreEqual("192.168.1.55:53317", item.EndpointText);
        CollectionAssert.Contains(changedProperties, nameof(LocalSendSendDeviceItem.FingerprintTag));
        CollectionAssert.Contains(changedProperties, nameof(LocalSendSendDeviceItem.EndpointText));
    }

    [TestMethod]
    public void EndpointText_ShowsThePortThatTellsTwoUserSessionsApart()
    {
        var apple = new LocalSendSendDeviceItem(new LocalSendDeviceInfo { Alias = "Apple", IpAddress = "192.168.1.20", Fingerprint = "apple", Port = 53317 });
        var grape = new LocalSendSendDeviceItem(new LocalSendDeviceInfo { Alias = "Grape", IpAddress = "192.168.1.20", Fingerprint = "grape", Port = 53318 });

        Assert.AreEqual("192.168.1.20:53317", apple.EndpointText);
        Assert.AreEqual("192.168.1.20:53318", grape.EndpointText);
        // The #tag is an octet of the address, so on one machine it cannot separate the two sessions; the
        // port is what the row has to show for them to read differently.
        Assert.AreEqual(apple.FingerprintTag, grape.FingerprintTag);
    }

    [TestMethod]
    public void EndpointText_BracketsALiteralIpv6Address()
    {
        var item = new LocalSendSendDeviceItem(new LocalSendDeviceInfo { Alias = "Peer", IpAddress = "fe80::42", Fingerprint = "peer", Port = 53317 });

        Assert.AreEqual("[fe80::42]:53317", item.EndpointText);
    }
}
