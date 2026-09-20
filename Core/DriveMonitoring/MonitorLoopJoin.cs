namespace Lertaro.Core.DriveMonitoring;

// Bounded wait for a drive monitor's own loop to finish before the per-drive index it writes into is
// disposed.
//
// Why it exists: a USN monitor's loop runs as a fire-and-forget Task that applies record batches into its
// drive's LiveIndex. UsnService.OnStop disposes that LiveIndex right after the monitors, and
// LiveIndex.Dispose reached ReaderWriterLockSlim.Dispose while the loop still held or awaited the same
// lock -- which throws SynchronizationLockException straight out of OnStop, terminates the whole service
// process (Windows then logs "Failed to stop service"), and was seen ten times in one machine's
// Application log between 2026-09-08 and 2026-09-19. Waiting for the loop here is what keeps that write
// out of a disposed index; LiveIndex.Dispose no longer letting that exception escape is the second,
// independent half of the same fix.
//
// Bounded on purpose: a service stop must never hang. The loops observe cancellation at their awaits
// (100-2000 ms Task.Delay calls plus a per-iteration token check), so the normal case returns in
// milliseconds; only a loop parked inside a synchronous FSCTL read can exceed the bound, and that case is
// logged rather than waited on forever. Four drives at this bound still sit well inside Windows' own
// service-stop timeout.
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
