namespace Lertaro.Core.Services.LocalSend;

/// <summary>
/// The one thing a task handed to Task.WaitAsync needs and does not get.
/// </summary>
internal static class LocalSendAbandonedTask
{
    /// <summary>
    /// Keep <paramref name="pending"/>'s fault observed even when a timeout makes nobody await it.
    /// </summary>
    /// <remarks>
    /// WaitAsync returns a TimeoutException and leaves the original read running; the stream then gets
    /// disposed under it, so the abandoned read faults with an IOException that no caller ever looks at.
    /// Some time later the finalizer raises that as TaskScheduler.UnobservedTaskException, and a transfer
    /// that merely stalled is reported to the user as a critical crash. Reading Exception off a faulted
    /// task is what marks it observed, which is what the continuation below exists to do -- the same shape
    /// Core/Indexer/NetworkDrive/Walk/TreeBuilder already uses for workers WaitAll leaves behind.
    /// </remarks>
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
