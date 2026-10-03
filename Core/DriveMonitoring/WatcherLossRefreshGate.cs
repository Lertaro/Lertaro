namespace Lertaro.Core.DriveMonitoring;

// Whether losing a drive's FileSystemWatcher still needs a refresh requested. The watcher itself is real
// OS plumbing with no injectable seam -- FileSystemWatcher is constructed inside DriveWatcherHost and its
// Error event fires on a pool thread -- so this one decision is what gets extracted and tested: a lost
// watcher has silently dropped every change that happened while it was down, and only a re-walk can
// recover them, so the loss must request exactly ONE refresh even though the retry path can raise several
// errors before monitoring comes back. The next successful (re)establishment re-arms the drive, because a
// later loss is a new gap in the event stream rather than a late error from the old one.
//
// Shared by both watcher owners -- WatcherManager (network/WSL/folder-index) and FolderDriveMonitor
// (FAT32/exFAT local volumes) -- so "one refresh per loss episode" cannot drift between them.
internal sealed class WatcherLossRefreshGate
{
    private readonly HashSet<string> _lost = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    // True the first time this drive reports a loss since the last Recovered call.
    public bool ShouldRequestRefresh(string drive)
    {
        lock (_gate)
            return _lost.Add(drive);
    }

    // The watcher is back up (or a new one is being configured): the next loss is a new episode.
    // ponytail: a watcher that flaps -- dies again within the retry delay of coming back -- re-arms here on
    // every re-establishment, so a permanently flapping drive asks for one refresh per flap. The ceiling is
    // the caller's own per-drive refresh debounce/dedupe (SchedulerQueueRunner, SearchEngineDriveMaintenance),
    // which keeps that to at most one walk per retry interval; the upgrade path is to require a minimum
    // uptime before re-arming.
    public void Recovered(string drive)
    {
        lock (_gate)
            _lost.Remove(drive);
    }
}
