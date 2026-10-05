using Lertaro.Core.DriveMonitoring;

namespace Lertaro.Core.Tests.DriveMonitoring;

[TestClass]
public sealed class KeyedDebouncerTests
{
    // Short enough to keep the suite fast, long enough to reliably observe coalescing on a busy CI box.
    private const int DelayMs = 60;
    private const int WaitTimeoutMs = 3000;

    [TestMethod]
    public void Schedule_SingleCall_FiresAfterDelay()
    {
        using var debouncer = new KeyedDebouncer<string>(DelayMs);
        using var fired = new ManualResetEventSlim(false);

        debouncer.Schedule("a", () => fired.Set());

        Assert.IsTrue(fired.Wait(WaitTimeoutMs));
    }

    [TestMethod]
    public void Schedule_RepeatedCallsForSameKeyWithinDelay_CoalesceIntoOneFiring()
    {
        using var debouncer = new KeyedDebouncer<string>(DelayMs);
        using var fired = new ManualResetEventSlim(false);
        var callCount = 0;

        for (var i = 0; i < 5; i++)
        {
            debouncer.Schedule("a", () =>
            {
                Interlocked.Increment(ref callCount);
                fired.Set();
            });
        }

        Assert.IsTrue(fired.Wait(WaitTimeoutMs));
        Thread.Sleep(DelayMs * 2); // give any wrongly-fired extra timers a chance to show up
        Assert.AreEqual(1, Volatile.Read(ref callCount));
    }

    [TestMethod]
    public void Schedule_DifferentKeys_FireIndependently()
    {
        using var debouncer = new KeyedDebouncer<string>(DelayMs);
        using var firedA = new ManualResetEventSlim(false);
        using var firedB = new ManualResetEventSlim(false);

        debouncer.Schedule("a", () => firedA.Set());
        debouncer.Schedule("b", () => firedB.Set());

        Assert.IsTrue(firedA.Wait(WaitTimeoutMs));
        Assert.IsTrue(firedB.Wait(WaitTimeoutMs));
    }

    [TestMethod]
    public void Cancel_BeforeDelayElapses_PreventsTheActionFromFiring()
    {
        using var debouncer = new KeyedDebouncer<string>(DelayMs);
        var fired = false;

        debouncer.Schedule("a", () => Volatile.Write(ref fired, true));
        debouncer.Cancel("a");

        Thread.Sleep(DelayMs * 3);
        Assert.IsFalse(Volatile.Read(ref fired));
    }

    [TestMethod]
    public void Cancel_UnknownKey_DoesNotThrow()
    {
        using var debouncer = new KeyedDebouncer<string>(DelayMs);
        debouncer.Cancel("never-scheduled");
    }

    [TestMethod]
    public void Dispose_WithPendingSchedules_PreventsAnyOfThemFromFiring()
    {
        var debouncer = new KeyedDebouncer<string>(DelayMs);
        var fired = false;

        debouncer.Schedule("a", () => Volatile.Write(ref fired, true));
        debouncer.Dispose();

        Thread.Sleep(DelayMs * 3);
        Assert.IsFalse(Volatile.Read(ref fired));
    }
}
