using Lertaro.Core.Services.LocalSend;
using Lertaro.Core.Services.LocalSend.Models;

namespace Lertaro.Core.Tests.Services.LocalSend;

[TestClass]
public sealed class LocalSendDiscoveryServiceTests
{
    [TestMethod]
    public void Start_DoesNotOverwriteAdvertisedTcpPort()
    {
        using var discovery = new LocalSendDiscoveryService
        {
            LocalInfo = new LocalSendDeviceInfo { Fingerprint = "local", Port = 61799 }
        };

        discovery.Start(0);

        Assert.AreEqual(61799, discovery.LocalInfo.Port);
    }

    [TestMethod]
    public void AddDiscoveredDevice_KeepsOneDeviceWhenItAnswersFromANewAddress()
    {
        using var discovery = new LocalSendDiscoveryService { LocalInfo = new LocalSendDeviceInfo { Fingerprint = "local" } };
        discovery.AddDiscoveredDevice(new LocalSendDeviceInfo { Alias = "Phone", IpAddress = "192.168.1.20", Fingerprint = "peer", Port = 53317 });
        discovery.AddDiscoveredDevice(new LocalSendDeviceInfo { Alias = "Phone", IpAddress = "192.168.1.55", Fingerprint = "peer", Port = 53317 });

        var devices = discovery.DiscoveredDevices;

        Assert.HasCount(1, devices);
        Assert.AreEqual("192.168.1.55", devices.Single().IpAddress);
    }

    [TestMethod]
    public void AddDiscoveredDevice_UsesPortAndFingerprintAsTheIdentity()
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

    [TestMethod]
    public void AddDiscoveredDevice_KeepsTheStackTheRowIsAlreadyOn()
    {
        using var discovery = new LocalSendDiscoveryService { LocalInfo = new LocalSendDeviceInfo { Fingerprint = "local" } };
        discovery.AddDiscoveredDevice(new LocalSendDeviceInfo { Alias = "Peer", IpAddress = "192.168.1.20", Fingerprint = "peer", Port = 53317 });

        // The same machine answering from its IPv6 side is not a move, and following it would flip the row
        // and the send target once per announcement burst.
        discovery.AddDiscoveredDevice(new LocalSendDeviceInfo { Alias = "Peer", IpAddress = "fe80::42", Fingerprint = "peer", Port = 53317 });

        Assert.AreEqual("192.168.1.20", discovery.DiscoveredDevices.Single().IpAddress);

        discovery.AddDiscoveredDevice(new LocalSendDeviceInfo { Alias = "Peer", IpAddress = "192.168.1.77", Fingerprint = "peer", Port = 53317 });

        Assert.AreEqual("192.168.1.77", discovery.DiscoveredDevices.Single().IpAddress);
    }

    [TestMethod]
    public void ShouldTakeAddress_WithoutParsableAddressesTakesTheNewOne()
    {
        Assert.IsTrue(LocalSendDiscoveryService.ShouldTakeAddress(string.Empty, "fe80::42"));
        Assert.IsTrue(LocalSendDiscoveryService.ShouldTakeAddress("not an address", "192.168.1.9"));
    }

    [TestMethod]
    public void AddDiscoveredDevice_WithoutAFingerprintIsKnownOnlyByItsAddress()
    {
        using var discovery = new LocalSendDiscoveryService { LocalInfo = new LocalSendDeviceInfo { Fingerprint = "local" } };
        discovery.AddDiscoveredDevice(new LocalSendDeviceInfo { Alias = "Silent one", IpAddress = "192.168.1.20", Fingerprint = "", Port = 53317 });
        discovery.AddDiscoveredDevice(new LocalSendDeviceInfo { Alias = "Silent two", IpAddress = "192.168.1.31", Fingerprint = "", Port = 53317 });
        discovery.AddDiscoveredDevice(new LocalSendDeviceInfo { Alias = "Silent one again", IpAddress = "192.168.1.20", Fingerprint = "", Port = 53317 });

        Assert.HasCount(2, discovery.DiscoveredDevices);
    }
}
