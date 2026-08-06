namespace Lertaro.Core.Indexer.NetworkDrive.Scheduling;

// Runs work on a dedicated, Lowest-priority thread instead of the shared ThreadPool. A large/slow
// background scan can otherwise saturate the ThreadPool (many workers doing blocking sync I/O) and starve
// unrelated Task.Run-based work -- including the app's own interactive search/launch code -- from getting
// a worker thread promptly. Lowest priority additionally lets the OS scheduler favor foreground UI work
// whenever there's real CPU contention -- most of a scan is network I/O wait (priority barely matters
// there), but TreeDiffBaseline's reuse-copy path on a resumed scan is a tight, genuinely CPU-bound loop
// with no I/O to yield on, which is exactly when this matters most. Neither change reduces throughput when
// the system isn't contended: idle cores run Lowest-priority threads exactly as fast as Normal ones.
// Wrapped back into a Task so callers keep normal Task.WaitAll/cancellation/exception-propagation semantics.
internal static class DedicatedWorkerThread
{
    public static Task Run(Func<Task> work, string name)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                work().GetAwaiter().GetResult();
                tcs.SetResult();
            }
            catch (OperationCanceledException)
            {
                // A cancelled scan (drive removed from config, or a user-initiated Stop) is expected, not
                // an error -- Task.WaitAll(tasks, token) in TreeBuilder.Run() throws and returns as soon as
                // its own token is cancelled, without waiting for every worker task to finish, so nothing
                // else ever observes this task's outcome. Faulting it (the old behavior) left it to be
                // garbage-collected as an unobserved exception, crashing the process via
                // TaskScheduler.UnobservedTaskException; a Canceled task never triggers that.
                tcs.SetCanceled();
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        })
        {
            IsBackground = true,
            Priority = ThreadPriority.Lowest,
            Name = name
        };
        thread.Start();
        return tcs.Task;
    }
}
