using Lertaro.Core.DriveMonitoring;

namespace Lertaro.Core.Indexer.Usn;

// Drive-monitor lifecycle for UsnIndexer, as extension methods (matching UsnIndexerExtensions/
// UsnIndexerCacheExtensions/UsnIndexerBuildExtensions' own split) to keep UsnIndexer.cs under the
// project's line limit.
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

    // Stops (without replacing) whatever monitor is currently registered for this one drive, if any --
    // called right before a single-drive rebuild starts, but ONLY for a journal-backed (NTFS/ReFS) drive.
    // Its UsnMonitor has no missed-change tracking of its own (see ApplyUsnRecords, which only pins the
    // drive's durable watermark rather than queueing a replay of its own) because it doesn't need one --
    // JournalReader.IndexDrive captures the USN watermark before the walk starts, so the next
    // monitor (started fresh once the rebuild finishes) always replays from there regardless of what a
    // stale old monitor did or didn't apply meanwhile. Leaving it running through its own rebuild instead
    // would only add risk for no benefit: it's still the SAME UsnIndexer instance and drive key, so a
    // record arriving in the exact narrow window OnDriveCompleted spends between removing the old
    // LiveIndex and swapping in the fresh one would hit ApplyUsnRecords' own LiveIndex.Mutate call on an
    // already-disposed instance. (A non-journal FolderDriveMonitor drive deliberately does NOT call this
    // -- see ApplyFolderChange's own comment on why keeping ITS monitor alive throughout is what closes a
    // real gap for that case instead.)
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
