namespace Lertaro.Core.IndexV2;

// Tears down a LiveIndex's ReaderWriterLockSlim without letting a concurrent drive monitor turn a service
// stop into a process death.
//
// ReaderWriterLockSlim.Dispose throws SynchronizationLockException when a thread is still queued on the
// lock (a mere holder is accepted by the runtime -- the queued waiter is what it refuses), which is exactly
// the state a drive monitor lands in when it tries to apply a USN batch while the index is being torn down:
// UsnService.OnStop acquires the write lock to clear the snapshot, and the monitor queues behind it. That
// exception used to escape OnStop and terminate the whole service process, which Windows then reports as
// "Failed to stop service" (ten occurrences in one machine's Application log between 2026-09-08 and
// 2026-09-19).
//
// Why a separate class rather than an inline try/catch: the contended state is a narrow, timing-dependent
// window, so the decision is split out where a test can hand it a lock that genuinely has a holder or a
// waiter instead of hoping the race shows up. This class has no state of its own; it always operates on
// the one lock its caller owns.
internal static class LiveIndexLockTeardown
{
    // True when the lock really was disposed. False means a monitor was still queued on it: the lock is
    // left alive, which is harmless -- it is collected with the index, ReaderWriterLockSlim.Dispose throws
    // before it changes any state (so the queued monitor still gets its batch through), and the index's own
    // _snapshot is already null, so any later Mutate() gets the ObjectDisposedException its callers already
    // handle.
    internal static bool TryDispose(ReaderWriterLockSlim lockToDispose)
    {
        try
        {
            lockToDispose.Dispose();
            return true;
        }
        catch (SynchronizationLockException ex)
        {
            Logger.Log($"LiveIndex.Dispose: a monitor thread was still using the index ({ex.Message}); the lock was left undisposed rather than failing the service stop.", LogLevel.Warn);
            return false;
        }
    }
}
