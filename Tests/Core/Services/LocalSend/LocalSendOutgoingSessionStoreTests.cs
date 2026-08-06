using Lertaro.Core.Services.LocalSend;

namespace Lertaro.Core.Tests.Services.LocalSend;

[TestClass]
public sealed class LocalSendOutgoingSessionStoreTests
{
    [TestMethod]
    public void TryCancel_V2MatchingRemoteSession_CancelsTheTransfer()
    {
        var store = new LocalSendOutgoingSessionStore();
        using var session = store.Start("192.168.1.20", "remote-session", legacy: false);

        var canceled = store.TryCancel("remote-session", "192.168.1.20", v2: true);

        Assert.IsTrue(canceled);
        Assert.IsTrue(session.Cancellation.IsCancellationRequested);
    }

    [TestMethod]
    public void TryCancel_AlreadyCanceledTransfer_IsRejected()
    {
        var store = new LocalSendOutgoingSessionStore();
        using var session = store.Start("192.168.1.20", "remote-session", legacy: false);
        Assert.IsTrue(store.TryCancel("remote-session", "192.168.1.20", v2: true));

        var canceledAgain = store.TryCancel("remote-session", "192.168.1.20", v2: true);

        Assert.IsFalse(canceledAgain);
    }

    [TestMethod]
    public void TryCancel_V1RequiresExactlyOneLegacyTransfer()
    {
        var store = new LocalSendOutgoingSessionStore();
        using var first = store.Start("192.168.1.20", null, legacy: true);
        using var second = store.Start("192.168.1.20", null, legacy: true);

        var canceled = store.TryCancel(null, "192.168.1.20", v2: false);

        Assert.IsFalse(canceled);
        Assert.IsFalse(first.Cancellation.IsCancellationRequested);
        Assert.IsFalse(second.Cancellation.IsCancellationRequested);
    }

    [TestMethod]
    public void TryCancel_WrongSender_DoesNotCancelTheTransfer()
    {
        var store = new LocalSendOutgoingSessionStore();
        using var session = store.Start("192.168.1.20", "remote-session", legacy: false);

        var canceled = store.TryCancel("remote-session", "192.168.1.21", v2: true);

        Assert.IsFalse(canceled);
        Assert.IsFalse(session.Cancellation.IsCancellationRequested);
    }
}
