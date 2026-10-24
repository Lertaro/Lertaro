namespace Lertaro.Core.DriveMonitoring;

// Waits for a removed drive's letter to come back. The volume arrival notification (VolumeArrivalMonitor)
// is the primary signal; the timed probe stays as a fallback for when registration is refused or an event
// is lost, so the previous two-second poll is no longer the only way a reinserted drive is noticed.
internal static class DriveReattachWaiter
{
    // Fallback probe interval while notifications work: the event path normally reacts with the mount
    // manager's own latency, so this only bounds how long a lost event can delay the recovery.
    internal static readonly TimeSpan FallbackProbeInterval = TimeSpan.FromSeconds(15);

    // The historical interval, used when the notification could not be registered. There the probe is the
    // only signal, so it stays tight.
    internal static readonly TimeSpan UnregisteredProbeInterval = TimeSpan.FromSeconds(2);

    // The drive letter can trail the volume interface by a moment (mount manager assigns it as part of
    // bringing the volume up), and a spun-up removable drive answers slowly, so probe briefly after an
    // arrival instead of going straight back to waiting.
    private static readonly TimeSpan ArrivalProbeInterval = TimeSpan.FromMilliseconds(250);
    private const int ArrivalProbeCount = 8;

    public static void Start(string drive, CancellationToken token, Action onReattached) => _ = Task.Run(async () =>
    {
        VolumeArrivalMonitor.EnsureStarted();
        var probeInterval = VolumeArrivalMonitor.IsRegistered ? FallbackProbeInterval : UnregisteredProbeInterval;

        using var wake = new SemaphoreSlim(0, 1);
        void OnArrived(object? sender, EventArgs e)
        {
            try { wake.Release(); }
            catch (SemaphoreFullException) { /* One pending wake is enough; the probe re-checks anyway. */ }
        }

        VolumeArrivalMonitor.Arrived += OnArrived;
        try
        {
            await WatchAsync(
                drive,
                () => VolumeHelper.DetectIndexableLocalDrives().Contains(drive, StringComparer.OrdinalIgnoreCase),
                ct => wake.WaitAsync(probeInterval, ct),
                onReattached,
                token).ConfigureAwait(false);
        }
        finally
        {
            VolumeArrivalMonitor.Arrived -= OnArrived;
        }
    }, token);

    /// <summary>
    /// Probes until the drive is present, then calls <paramref name="onReattached"/> once.
    /// </summary>
    /// <param name="waitForArrival">
    /// Completes when an arrival event fires or the fallback interval elapses; <c>true</c> means an event
    /// woke the loop, so the letter may need a few extra probes.
    /// </param>
    internal static async Task WatchAsync(
        string drive,
        Func<bool> isPresent,
        Func<CancellationToken, Task<bool>> waitForArrival,
        Action onReattached,
        CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                if (isPresent())
                {
                    onReattached();
                    return;
                }

                if (!await waitForArrival(token).ConfigureAwait(false))
                    continue;

                for (var attempt = 0; attempt < ArrivalProbeCount; attempt++)
                {
                    await Task.Delay(ArrivalProbeInterval, token).ConfigureAwait(false);
                    if (isPresent())
                    {
                        onReattached();
                        return;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                Logger.Log($"[DeviceNotification] Failed while waiting for drive {drive} to return: {ex.Message}", LogLevel.Warn);
                try { await Task.Delay(TimeSpan.FromSeconds(2), token).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
            }
        }
    }
}

