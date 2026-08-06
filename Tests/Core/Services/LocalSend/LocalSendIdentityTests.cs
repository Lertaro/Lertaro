using Lertaro.Core;
using Lertaro.Core.Services.LocalSend;

namespace Lertaro.Core.Tests.Services.LocalSend;

[TestClass]
public sealed class LocalSendIdentityTests
{
    [TestMethod]
    public void EnsureFingerprint_ReusesExistingFingerprint()
    {
        var settings = new LocalSendSettingsModel { DeviceFingerprint = "existing-fingerprint" };

        var fingerprint = LocalSendIdentity.EnsureFingerprint(settings);

        Assert.AreEqual("existing-fingerprint", fingerprint);
    }

    [TestMethod]
    public void CreateDeviceInfo_UsesTheConfiguredFingerprint()
    {
        var settings = new LocalSendSettingsModel { DeviceFingerprint = "stable-fingerprint", Port = 54321 };

        var device = LocalSendIdentity.CreateDeviceInfo(settings, "Test Device");

        Assert.AreEqual("stable-fingerprint", device.Fingerprint);
        Assert.AreEqual(54321, device.Port);
    }

    [TestMethod]
    public void CreateDeviceInfo_AdvertisesHttpsWhenEnabled()
    {
        var device = LocalSendIdentity.CreateDeviceInfo(new LocalSendSettingsModel { EnableHttps = true }, "Test Device");

        Assert.AreEqual("https", device.Protocol);
    }
}
