using Lertaro.Core.Services.LocalSend;

namespace Lertaro.Core.Tests.Services.LocalSend;

[TestClass]
public sealed class LocalSendApiRouteTests
{
    [TestMethod]
    public void BuildUri_UsesLegacyPathsForV1Peers()
    {
        var uri = LocalSendApiRoute.BuildUri("192.168.1.20", 53317, false, "prepare-upload", "1.0");

        Assert.AreEqual("http://192.168.1.20:53317/api/localsend/v1/send-request", uri.AbsoluteUri);
    }

    [TestMethod]
    public void BuildUri_UsesV2PathsForCurrentPeers()
    {
        var uri = LocalSendApiRoute.BuildUri("192.168.1.20", 53317, true, "upload", "2.1");

        Assert.AreEqual("https://192.168.1.20:53317/api/localsend/v2/upload", uri.AbsoluteUri);
    }
}
