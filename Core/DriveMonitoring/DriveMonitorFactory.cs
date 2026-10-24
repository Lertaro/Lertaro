using Lertaro.Core.Indexer.Usn;

namespace Lertaro.Core.DriveMonitoring;

// Single choke point for starting a drive's live monitor (USN journal or folder watcher), used by cold
// start (SearchEngineInitializer), a manual per-drive rebuild (SearchEngineDriveMaintenance), and hot-plug
// recovery (DriveRecovery) alike -- previously each of those three call sites independently constructed
// its own UsnMonitor/FolderDriveMonitor with no per-drive bookkeeping anywhere, so restarting a drive's
// monitor (e.g. clicking "重建" on an already-running drive) left the OLD one running forever alongside
// the new one: both kept reapplying the same USN/folder changes, and each was a separate, untracked
// source racing UsnIndexer.UpdateDriveCounts against whichever monitor's drive was actually mid-rebuild.
// Routing every call through here means UsnIndexer.RegisterDriveMonitor always stops the previous monitor
// for that drive first, so there is only ever one live per drive.
internal static class DriveMonitorFactory
{
    public static void EnsureMonitor(
        UsnIndexer indexer,
        string drive,
        ulong journalId,
        long nextUsn,
        CancellationToken parentToken,
        Action<string>? onReindexRequired,
        Action<string>? onRemovalRequested = null,
        Action<string>? onReindexAfterRemoval = null)
    {
        var fs = VolumeHelper.GetFileSystemType(drive);
        if (VolumeHelper.IsJournalCapableFileSystem(fs))
        {
            // Keep a linked source per monitor so disposal stops only THIS instance. UsnMonitor.Dispose
            // closes the volume handle immediately; cancellation then lets its loop unwind cleanly.
            var cts = CancellationTokenSource.CreateLinkedTokenSource(parentToken);
            var startUsn = ResolveStartUsn(drive, journalId, nextUsn);
            var monitor = new UsnMonitor(drive, journalId, startUsn, indexer, cts.Token, onReindexRequired);
            var removal = RegisterRemovalMonitor(indexer, drive, parentToken, onReindexRequired, onRemovalRequested, onReindexAfterRemoval);
            indexer.RegisterDriveMonitor(drive, new DriveMonitorRegistration(new CancellationDisposable(monitor, cts), removal));
            monitor.Start();
            return;
        }

        var folderMonitor = new FolderDriveMonitor(
            drive,
            (changeType, path, oldPath) => indexer.ApplyFolderChange(drive, changeType, path, oldPath),
            parentToken,
            // A FAT32/exFAT watcher that dies silently loses every change made while it was down; a
            // per-drive re-walk is the only way to reconcile, the same callback a removed-and-reattached
            // drive uses.
            () => onReindexRequired?.Invoke(drive));
        var folderRemoval = RegisterRemovalMonitor(indexer, drive, parentToken, onReindexRequired, onRemovalRequested, onReindexAfterRemoval);
        indexer.RegisterDriveMonitor(drive, new DriveMonitorRegistration(folderMonitor, folderRemoval));
        folderMonitor.Start();
    }

    // Every monitor start funnels through here -- cold start, per-drive rebuild, hot-plug recovery -- so
    // this is the one place that can stop a rebuild from handing its monitor a position the journal has
    // already dropped (see UsnJournalStart for why that used to cost a rebuild every time).
    private static long ResolveStartUsn(string drive, ulong journalId, long nextUsn)
    {
        if (UsnJournalInfo.TryQuery(drive) is not { } journal)
            return nextUsn;

        var decision = UsnJournalStart.Resolve(nextUsn, journalId, journal);
        if (!decision.SkippedGap)
            return decision.StartUsn;

        Logger.Log($"[Monitor] Drive {drive}: USN {nextUsn} is no longer in the journal (window {journal.FirstUsn}..{journal.NextUsn}); monitoring resumes from {decision.StartUsn} instead of re-indexing, leaving that gap to the next catch-up or rebuild.", LogLevel.Warn);
        return decision.StartUsn;
    }

    private static DriveDeviceRemovalMonitor? RegisterRemovalMonitor(
        UsnIndexer indexer,
        string drive,
        CancellationToken parentToken,
        Action<string>? onReindexRequired,
        Action<string>? onRemovalRequested,
        Action<string>? onReindexAfterRemoval) =>
        CreateRemovalMonitor(indexer, drive, parentToken, onReindexRequired, onRemovalRequested, onReindexAfterRemoval);

    private static DriveDeviceRemovalMonitor? CreateRemovalMonitor(
        UsnIndexer indexer,
        string drive,
        CancellationToken parentToken,
        Action<string>? onReindexRequired,
        Action<string>? onRemovalRequested,
        Action<string>? onReindexAfterRemoval)
    {
        void ReleaseDriveMonitor()
        {
            onRemovalRequested?.Invoke(drive);
            indexer.ReleaseDriveMonitor(drive);
            indexer.DropDriveFromRuntime(drive);
            indexer.SetDriveState(drive, "unavailable");
        }

        void ReleaseDrive()
        {
            indexer.RemoveDriveMonitor(drive);
            indexer.DropDriveFromRuntime(drive);
            indexer.SetDriveState(drive, "unavailable");
        }

        return DriveDeviceRemovalMonitor.Register(
            drive,
            ReleaseDriveMonitor,
            () => (onReindexAfterRemoval ?? onReindexRequired)?.Invoke(drive),
            () =>
            {
                ReleaseDrive();
                DriveReattachWaiter.Start(drive, parentToken, () => (onReindexAfterRemoval ?? onReindexRequired)?.Invoke(drive));
            });
    }

    private sealed class CancellationDisposable : IDisposable
    {
        private readonly UsnMonitor _monitor;
        private readonly CancellationTokenSource _cts;
        public CancellationDisposable(UsnMonitor monitor, CancellationTokenSource cts)
        {
            _monitor = monitor;
            _cts = cts;
        }

        public void Dispose()
        {
            _monitor.Dispose();
            // Cancel without Dispose: the monitor loop built on this token registers on it again
            // while winding down, and a disposed CTS turns that into ObjectDisposedException.
            // It holds no unmanaged resources, so skipping Dispose is safe.
            _cts.Cancel();
        }
    }
}
