using System.Text.Json;
using Lertaro.Core.Services.LocalSend;
using Lertaro.Core.Services.LocalSend.Models;

namespace Lertaro.Core.Tests.Services.LocalSend;

[TestClass]
public sealed class LocalSendProtocolMapperTests
{
    [TestMethod]
    public void CreateInfo_EmitsOnlyInfoEndpointFields()
    {
        var device = new LocalSendDeviceInfo
        {
            Alias = "Test device", Fingerprint = "fingerprint", Port = 54321, Protocol = "https", IsBusy = true
        };

        var json = JsonSerializer.Serialize(LocalSendProtocolMapper.CreateInfo(device));

        StringAssert.Contains(json, "\"alias\":\"Test device\"");
        Assert.IsFalse(json.Contains("port", StringComparison.Ordinal));
        Assert.IsFalse(json.Contains("protocol", StringComparison.Ordinal));
        Assert.IsFalse(json.Contains("isBusy", StringComparison.Ordinal));
    }

    [TestMethod]
    public void CreateMulticast_V22AnnouncementOmitsLegacyFlags()
    {
        var dto = LocalSendProtocolMapper.CreateMulticast(new LocalSendDeviceInfo(), announcement: true);
        var json = JsonSerializer.Serialize(dto);

        Assert.IsNull(dto.Announcement);
        Assert.IsNull(dto.Announce);
        Assert.IsFalse(json.Contains("announce", StringComparison.Ordinal));
    }

    [TestMethod]
    public void CreateMulticast_LegacyAnnouncementKeepsBothFlags()
    {
        var dto = LocalSendProtocolMapper.CreateMulticast(
            new LocalSendDeviceInfo { Version = "2.1" }, announcement: true);

        Assert.IsTrue(dto.Announcement);
        Assert.IsTrue(dto.Announce);
    }

    [TestMethod]
    public void IsAnnouncement_RecognizesFlaglessV22Message()
    {
        Assert.IsTrue(LocalSendProtocolMapper.IsAnnouncement(new LocalSendMulticastDto { Version = "2.2" }));
        Assert.IsFalse(LocalSendProtocolMapper.IsAnnouncement(new LocalSendMulticastDto { Version = "2.1" }));
    }

    [TestMethod]
    public void ToDevice_MissingProtocolVersion_UsesLegacyFallbacks()
    {
        var device = LocalSendProtocolMapper.ToDevice(
            new LocalSendMulticastDto { Alias = "Legacy", Fingerprint = "legacy-fingerprint" },
            "192.168.1.20", 53317, "https");

        Assert.AreEqual("1.0", device.Version);
        Assert.AreEqual(53317, device.Port);
        Assert.AreEqual("https", device.Protocol);
    }

    [TestMethod]
    public void ToDevice_UnknownProtocol_UsesTheOfficialHttpsEnumFallback()
    {
        var device = LocalSendProtocolMapper.ToDevice(
            new LocalSendMulticastDto { Alias = "Unknown protocol", Fingerprint = "fingerprint", Protocol = "future" },
            "192.168.1.20", 53317, "http");

        Assert.AreEqual("https", device.Protocol);
    }

    [TestMethod]
    public void ToDevice_UnknownDeviceType_UsesTheOfficialDesktopEnumFallback()
    {
        var device = LocalSendProtocolMapper.ToDevice(
            new LocalSendMulticastDto { Alias = "Unknown device", Fingerprint = "fingerprint", DeviceType = "future" },
            "192.168.1.20", 53317, "http");

        Assert.AreEqual("desktop", device.DeviceType);
    }
}
