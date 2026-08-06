using Lertaro.Core.IndexV2;
using Lertaro.Core.DriveMonitoring;

using Lertaro.Core.Indexer.Usn.Journal;
namespace Lertaro.Core.Indexer.Usn;

// Partial: the status types it publishes, and SnapshotStatus, live in UsnIndexerStatus.cs.
public partial class UsnIndexer : IDisposable
{
    public event Action<IndexerStatus>? StatusChanged;
    private long _lastProgressPublishTicks;

    internal readonly object _lockObj = new();
    internal readonly JournalReader _reader = new();
    internal readonly Dictionary<string, DriveRuntimeMetadata> _driveMetadata = new(StringComparer.OrdinalIgnoreCase);
    // Guarded by _lockObj for structural changes (add/remove a drive); each LiveIndex then guards its
    // own Snapshot/DeltaOverlay pair independently -- see SearchCoordinator's header comment.
    internal readonly Dictionary<string, LiveIndex> _recordIndexes = new(StringComparer.OrdinalIgnoreCase);
    // One live monitor per drive -- see DriveMonitorFactory, the sole place that populates this. Managed
    // via UsnIndexerMonitorExtensions (Register/Remove/DisposeAll), internal rather than private so those
    // extension methods can reach it.
    internal readonly Dictionary<string, IDisposable> _driveMonitors = new(StringComparer.OrdinalIgnoreCase);
    // Debounces UsnIndexerExtensions.ApplyFolderChange's own disk persist -- see its own comment on why.
    internal readonly KeyedDebouncer<string> _folderChangeSaveDebounce = new(1000, StringComparer.OrdinalIgnoreCase);
    // Drives whose FolderDriveMonitor detected a change while a rebuild was in progress for that same
    // drive -- see UsnIndexerExtensions.ApplyFolderChange (sets it) and ConsumeMissedFolderChangeDuringRebuild
    // (consumes it to queue one follow-up refresh once the rebuild finishes).
    internal readonly HashSet<string> _missedFolderChangeDuringRebuild = new(StringComparer.OrdinalIgnoreCase);

    public IndexerStatus Status { get; } = new();
    public object LockObj => _lockObj;

    // JournalId/NextUsn here are the LIVE catch-up position, updated on every USN batch; a LiveIndex's
    // own Snapshot.JournalId/NextUsn only reflect the position as of its last compaction. The other
    // fields are immutable identity, carried straight through from the store that first built this drive.
    internal sealed class DriveRuntimeMetadata
    {
        public FileRecordSourceKind SourceKind { get; init; }
        public FileRecordIdKind IdKind { get; init; }
        public string FileSystemType { get; init; } = string.Empty;
        public uint VolumeSerialNumber { get; init; }
        public UInt128 RootId { get; init; }
        public ulong JournalId { get; set; }
        public long NextUsn { get; set; }
        // False for a mid-walk checkpoint or a scan interrupted before finishing -- see
        // UsnIndexerCacheExtensions.IsDriveIndexComplete, the local-drive counterpart of
        // NetworkIndexer.Configure's own IsComplete-gated cold-start resume.
        public bool IsComplete { get; init; }
    }


    public void SearchStreaming(string query, int limit, Action<SearchResult> onResult, CancellationToken token = default, string? directoryFilter = null) => SearchCoordinator.SearchStreaming(_recordIndexes, LockObj, query, limit, onResult, token, directoryFilter);

    public bool EnumerateDirectory(string path, bool recursive, string[]? patterns, int limit, Action<SearchResult> onResult, CancellationToken token = default)
        => SearchCoordinator.EnumerateDirectory(_recordIndexes, LockObj, path, recursive, patterns, limit, onResult, token);

    public void SetDriveStatuses(IEnumerable<DriveIndexStatus> drives)
    {
        lock (LockObj)
        {
            Status.Drives = drives.ToList();
        }
        PublishStatusChanged();
    }

    public void SetDriveState(string drive, string state) => SetDriveState(drive, state, false);

    public void SetDriveState(string drive, string state, bool resetCounts)
    {
        lock (LockObj)
        {
            var item = Status.Drives.FirstOrDefault(d => d.Drive.Equals(drive, StringComparison.OrdinalIgnoreCase));
            if (item == null)
                return;

            item.State = state;
            if (resetCounts)
            {
                item.Files = 0;
                item.Dirs = 0;
            }
        }
        PublishStatusChanged();
    }

    public void UpdateDriveProgress(string drive, int files, int dirs)
    {
        lock (LockObj)
        {
            var item = Status.Drives.FirstOrDefault(d => d.Drive.Equals(drive, StringComparison.OrdinalIgnoreCase));
            if (item == null)
                return;

            item.State = "indexing";
            item.Files = files;
            item.Dirs = dirs;
            if (Status.ActiveDrives.Count == 1 && Status.ActiveDrives.Contains(drive, StringComparer.OrdinalIgnoreCase))
                Status.Progress = Math.Min(95, Status.Progress + 1);
            else
                Status.Progress = Math.Min(99, Math.Max(Status.Progress, 1));
        }
        NotifyProgressChanged();
    }

    // Usn records and changes apply logic is extracted to UsnIndexerExtensions.cs


    public void CompactMemory()
    {
        try
        {
            System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            Win32Api.TrimWorkingSet();
        }
        catch { }
    }

    public void ClearCaches() => SearchCoordinator.ClearCaches();

    // IndexV2's Snapshot has no per-row path memo to clear -- GetFullPath rebuilds a path from cheap
    // mmap reads every call, so there's nothing to reclaim here. Kept as a no-op so callers (a search
    // window closing/hiding, mirroring ShellIconHelper.ClearCache()'s trigger points) don't need to
    // know that.
    public void ClearAllPathCaches()
    {
    }

    public void CompactStatusQueryMemory()
    {
        ClearCaches();
        CompactMemory();
    }

    public void UnloadRuntime()
    {
        lock (LockObj)
        {
            _driveMetadata.Clear();
            foreach (var live in _recordIndexes.Values)
                live.Dispose();
            _recordIndexes.Clear();
            Status.ActiveDrives.Clear();
            Status.TotalFiles = Status.Drives.Sum(d => d.Files);
            Status.TotalDirs = Status.Drives.Sum(d => d.Dirs);
            if (Status.State == "ready")
                Status.State = "idle";
        }
        PublishStatusChanged();
    }

    internal static DriveRuntimeMetadata CreateMetadata(FileRecordStore store) => new DriveRuntimeMetadata
    {
        SourceKind = store.SourceKind,
        IdKind = store.IdKind,
        FileSystemType = store.FileSystemType,
        VolumeSerialNumber = store.VolumeSerialNumber,
        RootId = store.RootId,
        JournalId = store.JournalId,
        NextUsn = store.NextUsn,
        IsComplete = store.IsComplete
    };

    private static UInt128 ToSourceLocalId(UInt128 value) => value;

    internal void UpdateTotalsFromRuntime()
    {
        var totals = _recordIndexes.Values.Select(r => r.GetCounts()).ToList();
        Status.TotalFiles = totals.Sum(t => t.Files);
        Status.TotalDirs = totals.Sum(t => t.Dirs);
    }

    // markReady:true is the authoritative "this drive is done" signal (OnDriveCompleted just swapped a
    // freshly-built LiveIndex into _recordIndexes[drive], or a cold-load just opened one) -- it always
    // applies. Everywhere else (ApplyUsnRecords/ApplyFolderChange, reacting to a routine journal/folder
    // change), a rebuild already in progress for this SAME drive owns Files/Dirs/State until IT finishes:
    // _recordIndexes[drive] still points at the OLD index for the whole rebuild (BuildDrives only swaps
    // it in at completion), so an ordinary change notification arriving mid-rebuild from that drive's own
    // monitor (still alive throughout, by design -- see DriveMonitorFactory) would otherwise overwrite the
    // in-progress scan's own reported progress with the stale old index's total and flip the row back to
    // "ready" early -- the exact up/down flicker this guard exists to prevent.
    internal void UpdateDriveCounts(string drive, bool markReady = false)
    {
        var item = Status.Drives.FirstOrDefault(d => d.Drive.Equals(drive, StringComparison.OrdinalIgnoreCase));
        if (item == null)
            return;

        if (!markReady && item.State == "indexing")
            return;

        if (_recordIndexes.TryGetValue(drive, out var live))
        {
            var (files, dirs) = live.GetCounts();
            item.Files = files;
            item.Dirs = dirs;
        }
        item.State = "ready";
    }

    public void Dispose()
    {
        this.DisposeAllDriveMonitors();
        _folderChangeSaveDebounce.Dispose();
        _driveMetadata.Clear();
        foreach (var live in _recordIndexes.Values)
            live.Dispose();
        _recordIndexes.Clear();
    }

    internal void PublishStatusChanged()
    {
        try
        {
            StatusChanged?.Invoke(SnapshotStatus());
        }
        catch
        {
        }
    }

    public void NotifyStatusChanged() => PublishStatusChanged();

    public void NotifyProgressChanged()
    {
        var now = Environment.TickCount64;
        var last = Interlocked.Read(ref _lastProgressPublishTicks);
        if (now - last < 100)
            return;

        Interlocked.Exchange(ref _lastProgressPublishTicks, now);
        PublishStatusChanged();
    }
}
