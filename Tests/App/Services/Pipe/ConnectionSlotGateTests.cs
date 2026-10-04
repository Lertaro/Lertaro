using Lertaro.App.Services.Pipe;

namespace Lertaro.App.Tests.Services.Pipe;

// Every accepted connection to the App search pipe gets a handler that may run a full search over the
// App's initialized state, so the number of live handlers has to be bounded. These pin the admission
// decision itself; the pipe wiring around it is not testable from a test host, whose client would be
// rejected by the pipe ACL anyway.
[TestClass]
public sealed class ConnectionSlotGateTests
{
    [TestMethod]
    public void TryAcquire_AdmitsUpToTheLimitAndRefusesTheNext()
    {
        var gate = new ConnectionSlotGate(2);

        Assert.IsTrue(gate.TryAcquire());
        Assert.IsTrue(gate.TryAcquire());
        Assert.IsFalse(gate.TryAcquire());
    }

    [TestMethod]
    public void Release_FreesExactlyOneSlot()
    {
        var gate = new ConnectionSlotGate(1);

        Assert.IsTrue(gate.TryAcquire());
        Assert.IsFalse(gate.TryAcquire());

        gate.Release();

        Assert.IsTrue(gate.TryAcquire());
        Assert.IsFalse(gate.TryAcquire());
    }

    [TestMethod]
    public void TryAcquire_AtTheLimit_RefusesImmediatelyRatherThanQueueing()
    {
        // Queueing an over-limit client would keep spending the very resource the cap exists to protect:
        // each queued client still holds its connection open. The refusal has to be synchronous.
        var gate = new ConnectionSlotGate(1);
        Assert.IsTrue(gate.TryAcquire());

        var start = Environment.TickCount64;
        var admitted = gate.TryAcquire();

        Assert.IsFalse(admitted);
        Assert.IsLessThan(1_000, Environment.TickCount64 - start);
    }

    [TestMethod]
    public void Constructor_NonPositiveLimit_IsRejected()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new ConnectionSlotGate(0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new ConnectionSlotGate(-1));
    }
}
