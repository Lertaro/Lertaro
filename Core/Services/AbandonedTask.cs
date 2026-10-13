namespace Lertaro.Core.Services;

/// <summary>
/// The one thing a task handed to Task.WaitAsync needs and does not get.
/// </summary>
/// <remarks>
/// WaitAsync stops caring about the original task the moment its own timeout or cancellation fires: the
/// wait ends, nothing reads the original's result, and a task that faults later is never awaited by
/// anybody. Reading Exception off a faulted task is what marks it observed, which is what the continuation
/// below exists to do -- the same shape Core/Indexer/NetworkDrive/Walk/TreeBuilder already uses for workers
/// WaitAll leaves behind.
///
/// Without it the finalizer raises the fault as TaskScheduler.UnobservedTaskException, which
/// AppCrashHandler reports as a critical crash with a modal dialog: a search or a transfer that merely got
/// superseded is shown to the user as the app having crashed.
/// </remarks>
internal static class AbandonedTask
{
    /// <summary>
    /// Keep <paramref name="pending"/>'s fault observed even when the wait on it is abandoned.
    /// </summary>
    internal static Task<T> KeepObserved<T>(this Task<T> pending)
    {
        _ = pending.ContinueWith(
            faulted => _ = faulted.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return pending;
    }
}
