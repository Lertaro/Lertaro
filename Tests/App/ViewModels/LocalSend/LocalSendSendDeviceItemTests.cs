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
        CollectionAssert.Contains(changedProperties, nameof(LocalSendSendDeviceItem.FingerprintTag));
    }
}
