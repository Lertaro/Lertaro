using System.Net;
using Lertaro.Core.Services.LocalSend;
using Lertaro.Core.Services.LocalSend.Models;

namespace Lertaro.Core.Tests.Services.LocalSend;

[TestClass]
public sealed class LocalSendSubnetScannerTests
{
    [TestMethod]
    public void SelectInterfaces_UsesAtMostThreeAddresses()
    {
        var addresses = new[]
        {
            IPAddress.Parse("192.168.1.10"),
            IPAddress.Parse("192.168.2.10"),
            IPAddress.Parse("192.168.3.10"),
            IPAddress.Parse("192.168.4.10")
        };

        var selected = LocalSendSubnetScanner.SelectInterfaces(addresses);

        Assert.HasCount(3, selected);
        CollectionAssert.AreEqual(addresses.Take(3).ToArray(), selected.ToArray());
    }

    [TestMethod]
    public void BuildProbeAddresses_UsesRemainingHostsInTheSameSubnet()
    {
        var probes = LocalSendSubnetScanner.BuildProbeAddresses(IPAddress.Parse("192.168.42.7")).ToArray();

        Assert.HasCount(253, probes);
        Assert.IsFalse(probes.Contains("192.168.42.7"));
        Assert.IsTrue(probes.Contains("192.168.42.1"));
        Assert.IsTrue(probes.Contains("192.168.42.254"));
    }

    [TestMethod]
    public void BuildRegistrationUri_UsesV22AndTheConfiguredLocalProtocol()
    {
        var localInfo = new LocalSendDeviceInfo { Protocol = "https" };

        var uri = LocalSendSubnetScanner.BuildRegistrationUri(localInfo, "192.168.42.20", 53317);

        Assert.AreEqual("https://192.168.42.20:53317/api/localsend/v2/register", uri.AbsoluteUri);
    }
}
