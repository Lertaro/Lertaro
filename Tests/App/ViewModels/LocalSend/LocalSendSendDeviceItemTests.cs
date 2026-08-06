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
}
