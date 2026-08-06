using Lertaro.Core.Services.LocalSend;
using Lertaro.Core.Services.LocalSend.Models;

namespace Lertaro.Core.Tests.Services.LocalSend;

[TestClass]
public sealed class LocalSendAnnouncementResponderTests
{
    [TestMethod]
    public void BuildRegistrationUri_UsesPeerProtocolForIpv4()
    {
        var peer = new LocalSendDeviceInfo { IpAddress = "192.168.1.20", Port = 53317, Protocol = "https" };

        var uri = LocalSendAnnouncementResponder.BuildRegistrationUri(peer);

        Assert.AreEqual("https://192.168.1.20:53317/api/localsend/v2/register", uri.AbsoluteUri);
    }

    [TestMethod]
    public void BuildRegistrationUri_BracketsIpv6Addresses()
    {
        var peer = new LocalSendDeviceInfo { IpAddress = "fe80::1", Port = 53317, Protocol = "http" };

        var uri = LocalSendAnnouncementResponder.BuildRegistrationUri(peer);

        Assert.AreEqual("http://[fe80::1]:53317/api/localsend/v2/register", uri.AbsoluteUri);
    }

    [TestMethod]
    public void BuildRegistrationUri_UsesV1RouteForV1Peers()
    {
        var peer = new LocalSendDeviceInfo { IpAddress = "192.168.1.20", Port = 53317, Version = "1.0" };

        var uri = LocalSendAnnouncementResponder.BuildRegistrationUri(peer);

        Assert.AreEqual("http://192.168.1.20:53317/api/localsend/v1/register", uri.AbsoluteUri);
    }
}
