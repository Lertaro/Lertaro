namespace Lertaro.Core.Hook;

// Shared bounded STA dispatcher for plugin adapter/collector reads (IFileDialogAdapter,
// IInlineSearchAdapter, IActivePathCollector). Third-party plugin code can hang inside
// cross-process COM calls, so every such read from tracker machinery runs on a throwaway
// STA thread with a timeout instead of directly on the calling thread.
// Split out of ExplorerActivePathPoller so ExplorerWindowClassifier can share the exact
// same semantics; the abandoned-thread budget lives here for both callers.
internal static class ExplorerStaInvoker
{
    // Each timed-out read abandons a background STA thread that stays parked inside the hung
    // COM/shell call indefinitely. Cap the number of live abandoned threads so a wedged shell
    // extension cannot leak threads without bound; while at the cap, reads fail fast to the
    // fallback. The count is conservative: a thread that completes only after its caller timed
    // out may miss the decrement below, which just makes the cap trip slightly earlier.
    private const int MaxAbandonedThreads = 8;
    private static int _abandonedThreads;

    public static T RunOnStaWithTimeout<T>(Func<T> func, T fallback, TimeSpan timeout)
        => RunOnStaWithTimeout(func, fallback, timeout, out _);

    public static T RunOnStaWithTimeout<T>(Func<T> func, T fallback, TimeSpan timeout, out bool timedOut)
    {
        timedOut = false;
        if (Volatile.Read(ref _abandonedThreads) >= MaxAbandonedThreads)
        {
            Logger.Log("[ExplorerStaInvoker] Abandoned-thread budget exhausted; failing the read fast.", LogLevel.Warn);
            timedOut = true;
            return fallback;
        }

        var done = new ManualResetEventSlim(false);
        Exception? error = null;
        var result = fallback;
        var abandoned = 0;
        var thread = new Thread(() =>
        {
            try { result = func(); }
            catch (Exception ex) { error = ex; }
            finally
            {
                done.Set();
                // Ownership rule: the waiter disposes `done` only when it observed the Set.
                // On the timeout path the caller has already returned, so disposing here (in
                // the thread that outlives the call) is the only safe place left.
                if (Volatile.Read(ref abandoned) != 0)
                {
                    Interlocked.Decrement(ref _abandonedThreads);
                    done.Dispose();
                }
            }
        })
        {
            IsBackground = true,
            Name = "ExplorerStaInvoke"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        if (!done.Wait(timeout))
        {
            Volatile.Write(ref abandoned, 1);
            Interlocked.Increment(ref _abandonedThreads);
            timedOut = true;
            Logger.Log("[ExplorerStaInvoker] Plugin read timed out; continuing with fallback.", LogLevel.Warn);
        }
        else
        {
            done.Dispose();
            if (error != null)
                Logger.Log($"[ExplorerStaInvoker] Plugin read failed: {error.Message}", LogLevel.Warn);
        }

        return result;
    }
}
