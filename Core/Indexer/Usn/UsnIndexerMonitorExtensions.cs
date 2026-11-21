using Lertaro.Core.DriveMonitoring;

namespace Lertaro.Core.Indexer.Usn;

// Manages UsnIndexer drive monitors.
internal static class UsnIndexerMonitorExtensions
{
    // Stops and replaces whatever monitor was previously registered for this drive, if any -- called
    // exactly once per monitor start, from DriveMonitorFactory.EnsureMonitor. Disposed outside the lock:
    // FolderDriveMonitor.Dispose() tears down a real FileSystemWatcher, and a CancellationDisposable's
    // Cancel() can run arbitrary continuations -- neither should happen while holding LockObj.
    internal static void RegisterDriveMonitor(this UsnIndexer indexer, string drive, IDisposable monitor)
    {
        IDisposable? old;
        lock (indexer.LockObj)
        {
            indexer._driveMonitors.TryGetValue(drive, out old);
            (old as DriveMonitorRegistration)?.InvalidateCallbacks();
            indexer._driveMonitors[drive] = monitor;
        }
        old?.Dispose();
    }

    // Stops the current monitor before rebuilding a journal-backed drive. JournalReader records a
    // starting USN watermark, and the replacement monitor replays from it. Keeping the old monitor active
    // risks mutation during the index swap. Non-journal FolderDriveMonitor instances stay active to
    // capture changes that have no journal replay.
    internal static bool RemoveDriveMonitor(this UsnIndexer indexer, string drive, IDisposable? expected = null)
    {
        IDisposable? old;
        lock (indexer.LockObj)
        {
            if (!indexer._driveMonitors.TryGetValue(drive, out old) ||
                (expected != null && !ReferenceEquals(old, expected))) return false;
            (old as DriveMonitorRegistration)?.InvalidateCallbacks();
            indexer._driveMonitors.Remove(drive);
        }
        old.Dispose();
        return true;
    }

    // Releases only the live filesystem watcher while keeping the PnP notification registered until
    // Windows reports whether the removal completed or was rejected.
    internal static void ReleaseDriveMonitor(this UsnIndexer indexer, string drive)
    {
        DriveMonitorRegistration? registration;
        lock (indexer.LockObj)
            registration = indexer._driveMonitors.TryGetValue(drive, out var monitor)
                ? monitor as DriveMonitorRegistration
                : null;
        registration?.DisposeMonitor();
    }

    internal static bool TryInvokeRemovalCallback(this UsnIndexer indexer, string drive,
        DriveMonitorRegistration expected, DriveDeviceRemovalMonitor removal, Action action)
    {
        lock (indexer.LockObj)
            return indexer._driveMonitors.TryGetValue(drive, out var current) && ReferenceEquals(current, expected)
                && expected.TryInvokeRemovalCallback(removal, action);
    }

    internal static bool ReleaseDriveMonitor(this UsnIndexer indexer, string drive,
        DriveMonitorRegistration expected, DriveDeviceRemovalMonitor removal, Action? beforeStop = null)
    {
        if (!indexer.TryInvokeRemovalCallback(drive, expected, removal, () => beforeStop?.Invoke())) return false;
        // Stop only the captured instance, outside the locks. Any later cache mutation must recheck it.
        expected.DisposeMonitor();
        return true;
    }

    internal static bool TryRecoverDriveMonitor(this UsnIndexer indexer, string drive,
        DriveMonitorRegistration expected, Action recover)
    {
        lock (indexer.LockObj)
        {
            if (!indexer._driveMonitors.TryGetValue(drive, out var current) || !ReferenceEquals(current, expected))
                return false;
            recover();
            return true;
        }
    }

    // Stops every currently-registered monitor -- a full rebuild-from-scratch tearing down and restarting
    // everything, or final app shutdown.
    internal static void DisposeAllDriveMonitors(this UsnIndexer indexer)
    {
        List<IDisposable> monitors;
        lock (indexer.LockObj)
        {
            monitors = indexer._driveMonitors.Values.ToList();
            foreach (var monitor in monitors)
                (monitor as DriveMonitorRegistration)?.InvalidateCallbacks();
            indexer._driveMonitors.Clear();
        }
        foreach (var monitor in monitors)
            monitor.Dispose();
    }
}
