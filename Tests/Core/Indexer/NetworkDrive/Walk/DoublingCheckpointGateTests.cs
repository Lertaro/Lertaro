using Lertaro.Core.Indexer.NetworkDrive.Walk;

namespace Lertaro.Core.Tests.Indexer.NetworkDrive.Walk;

[TestClass]
public sealed class DoublingCheckpointGateTests
{
    [TestMethod]
    public void TryEnter_BelowThreshold_ReturnsFalse()
    {
        var gate = new DoublingCheckpointGate(initialBatchSize: 10, maxBatchSize: 100);

        for (var i = 0; i < 9; i++)
            Assert.IsFalse(gate.TryEnter());
    }

    [TestMethod]
    public void TryEnter_AtThreshold_ReturnsTrueExactlyOnce()
    {
        var gate = new DoublingCheckpointGate(initialBatchSize: 10, maxBatchSize: 100);

        for (var i = 0; i < 9; i++)
            gate.TryEnter();

        Assert.IsTrue(gate.TryEnter());
        // The counter was reset to 0 by the call above, so this next call's count is 1 -- nowhere near
        // the threshold yet, independent of Completed() ever being called.
        Assert.IsFalse(gate.TryEnter());
    }

    [TestMethod]
    public void TryEnter_ThresholdCrossedAgainBeforeCompleted_StaysBlockedUntilCompletedIsCalled()
    {
        var gate = new DoublingCheckpointGate(initialBatchSize: 5, maxBatchSize: 100);
        for (var i = 0; i < 4; i++)
            gate.TryEnter();
        Assert.IsTrue(gate.TryEnter()); // 5th call fires; checkpointInFlight now held, Completed() not called yet

        // BatchSize is still 5 (only Completed() doubles it), so 5 more calls reach the threshold again --
        // but the in-flight guard, not the count, is what blocks these (unlike the plain "just fired,
        // counter is low" case in TryEnter_AtThreshold_ReturnsTrueExactlyOnce).
        for (var i = 0; i < 4; i++)
            Assert.IsFalse(gate.TryEnter());
        Assert.IsFalse(gate.TryEnter());

        gate.Completed(); // releases the guard and doubles the gap to 10

        for (var i = 0; i < 9; i++)
            Assert.IsFalse(gate.TryEnter());
        Assert.IsTrue(gate.TryEnter());
    }

    [TestMethod]
    public void Completed_DoublesBatchSizeUntilCapped()
    {
        var gate = new DoublingCheckpointGate(initialBatchSize: 10, maxBatchSize: 35);
        Assert.AreEqual(10, gate.BatchSize);

        gate.Completed();
        Assert.AreEqual(20, gate.BatchSize);

        gate.Completed();
        Assert.AreEqual(35, gate.BatchSize); // 40 would exceed the 35 cap

        gate.Completed();
        Assert.AreEqual(35, gate.BatchSize); // stays capped
    }
}
