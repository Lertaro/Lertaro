using Lertaro.Core.Services.LocalSend;
using Lertaro.Core.Services.LocalSend.Models;

namespace Lertaro.Core.Tests.Services.LocalSend;

[TestClass]
public sealed class LocalSendSessionAuthorizationTests
{
    [TestMethod]
    public void TryCancel_V2AcceptedSessionWithoutSessionId_IsRejected()
    {
        var server = new LocalSendServer();
        Assert.IsTrue(server.TryRegisterActiveSession("session", new PrepareUploadRequestDto { Info = new LocalSendDeviceInfo { IpAddress = "192.168.1.20" } }));
        server.RegisterUploadAuthorization("session", "192.168.1.20", new Dictionary<string, string> { ["file"] = "token" });

        var canceled = LocalSendSessionAuthorization.TryCancel(server, null, "192.168.1.20", v2: true);

        Assert.IsFalse(canceled);
    }

    [TestMethod]
    public void TryCancel_V2WaitingSessionWithoutSessionId_IsAccepted()
    {
        var server = new LocalSendServer();
        Assert.IsTrue(server.TryRegisterActiveSession("session", new PrepareUploadRequestDto { Info = new LocalSendDeviceInfo { IpAddress = "192.168.1.20" } }));

        var canceled = LocalSendSessionAuthorization.TryCancel(server, null, "192.168.1.20", v2: true);

        Assert.IsTrue(canceled);
    }

    [TestMethod]
    public void TryCancel_AlreadyCanceledReceiveSession_IsRejected()
    {
        var server = new LocalSendServer();
        Assert.IsTrue(server.TryRegisterActiveSession("session", new PrepareUploadRequestDto { Info = new LocalSendDeviceInfo { IpAddress = "192.168.1.20" } }));
        Assert.IsTrue(LocalSendSessionAuthorization.TryCancel(server, "session", "192.168.1.20", v2: true));

        var canceledAgain = LocalSendSessionAuthorization.TryCancel(server, "session", "192.168.1.20", v2: true);

        Assert.IsFalse(canceledAgain);
    }
}
