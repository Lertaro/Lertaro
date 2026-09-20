using Lertaro.Core.IndexV2;

namespace Lertaro.Core.Tests.IndexV2;

// Deterministic coverage for the branch that used to kill the service process: ReaderWriterLockSlim.Dispose
// throws SynchronizationLockException while a monitor thread is queued on the index lock, and that
// exception escaped UsnService.OnStop. The end-to-end shape is a narrow timing window, so the lock is
// built by hand here and put into that state on purpose rather than raced for.
[TestClass]
public sealed class LiveIndexLockTeardownTests
{
    [TestMethod]
    public void UncontendedLock_IsDisposed()
    {
        var rwLock = new ReaderWriterLockSlim();

        Assert.IsTrue(LiveIndexLockTeardown.TryDispose(rwLock));

        // Really disposed, not silently skipped: every later use of a disposed index is expected to fail
        // this way, so covering it up would trade a crash for a silent no-op.
        Assert.ThrowsExactly<ObjectDisposedException>(rwLock.EnterReadLock);
    }

    [TestMethod]
    public void LockWithAQueuedMonitor_IsLeftAliveInsteadOfThrowing()
    {
        var rwLock = new ReaderWriterLockSlim();
        using var holderInside = new ManualResetEventSlim();
        using var releaseHolder = new ManualResetEventSlim();
        using var waiterStarted = new ManualResetEventSlim();

        // The position a drive monitor is in during the field crash: holding the write lock for the
        // duration of a USN batch ...
        var holder = new Thread(() =>
        {
            rwLock.EnterWriteLock();
            holderInside.Set();
            releaseHolder.Wait(TimeSpan.FromSeconds(30));
            rwLock.ExitWriteLock();
        });

        // ... while a second monitor arrives and queues on the same lock. A waiter is what
        // ReaderWriterLockSlim.Dispose actually refuses (a mere holder is accepted), so this is the state
        // the service-stop path has to survive.
        var waiter = new Thread(() =>
        {
            waiterStarted.Set();
            rwLock.EnterReadLock();
            rwLock.ExitReadLock();
        });

        holder.Start();
        Assert.IsTrue(holderInside.Wait(TimeSpan.FromSeconds(30)));
        waiter.Start();
        Assert.IsTrue(waiterStarted.Wait(TimeSpan.FromSeconds(30)));
        Assert.IsTrue(WaitUntilQueued(rwLock), "the second monitor never queued on the lock");

        try
        {
            // The whole point: no exception escapes here, so OnStop keeps running and the service stops.
            Assert.IsFalse(LiveIndexLockTeardown.TryDispose(rwLock));
        }
        finally
        {
            releaseHolder.Set();
            Assert.IsTrue(holder.Join(TimeSpan.FromSeconds(30)));
            Assert.IsTrue(waiter.Join(TimeSpan.FromSeconds(30)));
        }

        // The queued monitor finishes its batch through the still-live lock, and the next teardown (a later
        // stop, or GC) settles it -- which is why leaving it undisposed is the safe half of the trade.
        Assert.IsTrue(LiveIndexLockTeardown.TryDispose(rwLock));
    }

    // Reading the lock's own queue rather than sleeping a guessed interval: once a thread is queued it
    // stays queued until the holder releases, so this cannot go from true back to false.
    private static bool WaitUntilQueued(ReaderWriterLockSlim rwLock)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (rwLock.WaitingReadCount == 0)
        {
            if (DateTime.UtcNow > deadline)
                return false;
            Thread.Sleep(10);
        }
        return true;
    }
}
