using Lertaro.Core.Services.LocalSend;
using Lertaro.Core.Services.LocalSend.Models;

namespace Lertaro.Core.Tests.Services.LocalSend;

[TestClass]
public sealed class LocalSendDiscoveryServiceTests
{
    [TestMethod]
    public void AddDiscoveredDevice_UsesIpPortAndFingerprintAsTheIdentity()
    {
        using var discovery = new LocalSendDiscoveryService { LocalInfo = new LocalSendDeviceInfo { Fingerprint = "local" } };
        discovery.AddDiscoveredDevice(new LocalSendDeviceInfo { Alias = "Peer", IpAddress = "192.168.1.20", Fingerprint = "peer", Port = 53317 });
        discovery.AddDiscoveredDevice(new LocalSendDeviceInfo { Alias = "Different port", IpAddress = "192.168.1.20", Fingerprint = "peer", Port = 54444 });
        discovery.AddDiscoveredDevice(new LocalSendDeviceInfo { Alias = "Different fingerprint", IpAddress = "192.168.1.20", Fingerprint = "other-peer", Port = 53317 });
        discovery.AddDiscoveredDevice(new LocalSendDeviceInfo { Alias = "Peer", IpAddress = "192.168.1.20", Fingerprint = "peer", Port = 54444 });

        var devices = discovery.DiscoveredDevices;

        Assert.HasCount(3, devices);
        Assert.IsTrue(devices.Any(device => device.Port == 54444 && device.Fingerprint == "peer"));
        Assert.IsTrue(devices.Any(device => device.Port == 53317 && device.Fingerprint == "other-peer"));
    }
}
