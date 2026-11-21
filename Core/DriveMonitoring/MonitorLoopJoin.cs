namespace Lertaro.Core.DriveMonitoring;

// Waits for a drive monitor's loop before disposing the LiveIndex it updates. Otherwise a pending
// mutation can use a disposed ReaderWriterLockSlim and fail service shutdown.
//
// Bound the wait so shutdown cannot hang on a synchronous FSCTL call. Normal loops observe cancellation
// during delays and iteration checks; log a timeout rather than waiting indefinitely. LiveIndex disposal
// also contains shutdown exceptions.
internal static class MonitorLoopJoin
{
    internal static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(2);

    // Stops a monitor's loop for good: cancels its own token, waits for the loop to finish, and releases
    // the token source only once it really has. The cancel is not left to the caller on purpose -- the PnP
    // drive-removal path disposes a monitor without cancelling anything, and a join with nothing cancelled
    // would just burn the whole timeout.
    internal static bool Stop(Task loop, CancellationTokenSource loopCts, string drive, TimeSpan timeout = default)
    {
        try
        {
            loopCts.Cancel();
        }
        catch (AggregateException ex)
        {
            // A callback registered on the token threw. CancellationTokenSource.Cancel collects those into
            // an AggregateException, and letting it out here would put an exception back into the very
            // service-stop path this method exists to keep alive -- the loop is being stopped either way.
            Logger.Log($"[Monitor] Drive {drive} cancellation callbacks threw: {ex.GetBaseException().Message}", LogLevel.Warn);
        }

        if (!Wait(loop, drive, timeout))
            return false;

        // Safe only now that the loop has returned: it registers on this token again while unwinding (its
        // Task.Delay calls), and a disposed source turns that into ObjectDisposedException.
        loopCts.Dispose();
        return true;
    }

    internal static bool Wait(Task loop, string drive, TimeSpan timeout = default)
    {
        if (loop.IsCompleted)
            return true;

        var wait = timeout == default ? DefaultTimeout : timeout;
        try
        {
            if (loop.Wait(wait))
                return true;
        }
        catch (AggregateException ex)
        {
            // UsnMonitor's own wrapper catches what it can, so this is a safety net: a loop that ended in a
            // fault or was cancelled has ended, which is all this wait is for.
            Logger.Log($"[Monitor] Drive {drive} monitor loop ended with {ex.GetBaseException().Message}", LogLevel.Warn);
            return true;
        }

        Logger.Log($"[Monitor] Drive {drive} monitor loop still running after {wait.TotalSeconds:0.#}s; continuing without it.", LogLevel.Warn);
        return false;
    }
}
