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
        Action<string>? onReindexRequired)
    {
        var fs = VolumeHelper.GetFileSystemType(drive);
        if (VolumeHelper.IsJournalCapableFileSystem(fs))
        {
            // UsnMonitor has no per-instance stop of its own -- it only ever checks the CancellationToken
            // it was constructed with (see its own MonitorLoop). A linked source lets RegisterDriveMonitor
            // stop just THIS instance (Cancel) without touching the app-wide parent token every other
            // drive's monitor also derives from.
            var cts = CancellationTokenSource.CreateLinkedTokenSource(parentToken);
            new UsnMonitor(drive, journalId, nextUsn, indexer, cts.Token, onReindexRequired).Start();
            indexer.RegisterDriveMonitor(drive, new CancellationDisposable(cts));
            return;
        }

        var monitor = new FolderDriveMonitor(drive, (changeType, path, oldPath) => indexer.ApplyFolderChange(drive, changeType, path, oldPath), parentToken);
        monitor.Start();
        indexer.RegisterDriveMonitor(drive, monitor);
    }

    private sealed class CancellationDisposable : IDisposable
    {
        private readonly CancellationTokenSource _cts;
        public CancellationDisposable(CancellationTokenSource cts) => _cts = cts;

        public void Dispose()
        {
            _cts.Cancel();
            _cts.Dispose();
        }
    }
}
