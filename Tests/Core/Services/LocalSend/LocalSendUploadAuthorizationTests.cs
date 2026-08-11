using Lertaro.Core.Services.LocalSend;
using Lertaro.Core.Services.LocalSend.Models;

namespace Lertaro.Core.Tests.Services.LocalSend;

[TestClass]
public sealed class LocalSendUploadAuthorizationTests
{
    [TestMethod]
    public void Allows_MatchesTheIssuedTokenAndSenderIp()
    {
        var authorization = new LocalSendUploadAuthorization("192.168.1.20", new Dictionary<string, string> { ["file-1"] = "token-1" });

        var allowed = authorization.Allows("192.168.1.20", "file-1", "token-1");

        Assert.IsTrue(allowed);
    }

    [TestMethod]
    public void Allows_RejectsAnUnexpectedTokenOrSender()
    {
        var authorization = new LocalSendUploadAuthorization("192.168.1.20", new Dictionary<string, string> { ["file-1"] = "token-1" });

        Assert.IsFalse(authorization.Allows("192.168.1.21", "file-1", "token-1"));
        Assert.IsFalse(authorization.Allows("192.168.1.20", "file-1", "token-2"));
    }

    [TestMethod]
    public void UploadState_ChecksumMismatchAllowsThreeAttemptsThenEndsSession()
    {
        var authorization = new LocalSendUploadAuthorization(
            "192.168.1.20", new Dictionary<string, string> { ["file-1"] = "token-1" });

        Assert.IsTrue(authorization.TryBeginUpload("file-1"));
        Assert.IsFalse(authorization.CompleteUpload("file-1", LocalSendFileSaveStatus.ChecksumMismatch));
        Assert.IsTrue(authorization.TryBeginUpload("file-1"));
        Assert.IsFalse(authorization.CompleteUpload("file-1", LocalSendFileSaveStatus.ChecksumMismatch));
        Assert.IsTrue(authorization.TryBeginUpload("file-1"));
        Assert.IsTrue(authorization.CompleteUpload("file-1", LocalSendFileSaveStatus.ChecksumMismatch));
        Assert.IsFalse(authorization.TryBeginUpload("file-1"));
    }

    [TestMethod]
    public void UploadState_RejectsConcurrentUploadOfTheSameFile()
    {
        var authorization = new LocalSendUploadAuthorization(
            "192.168.1.20", new Dictionary<string, string> { ["file-1"] = "token-1" });

        Assert.IsTrue(authorization.TryBeginUpload("file-1"));
        Assert.IsFalse(authorization.TryBeginUpload("file-1"));
    }

    [TestMethod]
    public void TryCancelFromSender_RequiresTheOriginalSender()
    {
        var server = new LocalSendServer();
        server.RegisterActiveSession("session-1", new PrepareUploadRequestDto
        {
            Info = new LocalSendDeviceInfo { IpAddress = "192.168.1.20", Version = "2.1" }
        });

        Assert.IsFalse(LocalSendSessionAuthorization.TryCancel(server, "session-1", "192.168.1.21", v2: true));
        Assert.IsTrue(LocalSendSessionAuthorization.TryCancel(server, "session-1", "192.168.1.20", v2: true));
        Assert.IsTrue(server.IsSessionCanceled("session-1"));
    }

    [TestMethod]
    public void TryCancelFromSender_WithoutSessionIdCancelsThePendingV2Session()
    {
        var server = new LocalSendServer();
        server.RegisterActiveSession("session-1", new PrepareUploadRequestDto
        {
            Info = new LocalSendDeviceInfo { IpAddress = "192.168.1.20", Version = "2.1" }
        });

        var canceled = LocalSendSessionAuthorization.TryCancel(server, null, "192.168.1.20", v2: true);

        Assert.IsTrue(canceled);
        Assert.IsTrue(server.IsSessionCanceled("session-1"));
    }
}
