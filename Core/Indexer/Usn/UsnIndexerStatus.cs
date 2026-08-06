namespace Lertaro.Core.Indexer.Usn;

// The status UsnIndexer publishes, and the snapshot it publishes it as. Split out of UsnIndexer.cs
// purely to keep that file under the repo's line limit; these stay nested types of UsnIndexer so
// every caller keeps saying UsnIndexer.IndexerStatus.
public partial class UsnIndexer
{
    public class IndexerStatus
    {
        public string State { get; set; } = "idle";
        public int Progress { get; set; } = 0;
        public int TotalFiles { get; set; } = 0;
        public int TotalDirs { get; set; } = 0;
        public double ElapsedTime { get; set; } = 0.0;
        public bool IsMaintenanceBusy { get; set; }
        public List<string> ActiveDrives { get; set; } = new();
        public List<DriveIndexStatus> Drives { get; set; } = new();
    }

    public class DriveIndexStatus
    {
        public string Drive { get; set; } = string.Empty;
        public bool Enabled { get; set; }
        public string Kind { get; set; } = "LocalNtfs";
        public string State { get; set; } = "unknown";
        public int Files { get; set; }
        public int Dirs { get; set; }
        public string CachePath { get; set; } = string.Empty;
    }

    public IndexerStatus SnapshotStatus()
    {
        lock (LockObj)
        {
            return new IndexerStatus
            {
                State = Status.State,
                Progress = Status.Progress,
                TotalFiles = Status.TotalFiles,
                TotalDirs = Status.TotalDirs,
                ElapsedTime = Status.ElapsedTime,
                IsMaintenanceBusy = Status.IsMaintenanceBusy,
                ActiveDrives = Status.ActiveDrives.ToList(),
                Drives = Status.Drives.Select(d => new DriveIndexStatus
                {
                    Drive = d.Drive,
                    Enabled = d.Enabled,
                    Kind = d.Kind,
                    State = d.State,
                    Files = d.Files,
                    Dirs = d.Dirs,
                    CachePath = d.CachePath
                }).ToList()
            };
        }
    }

    /// <summary>
    /// Raised for every applied change batch: which drive, and the directories it landed in.
    /// <c>null</c> directories means the batch could not be pinned down and anything on that drive may
    /// have moved.
    /// </summary>
    /// <remarks>
    /// Raised, not recorded into the status. A batch is small and constant -- measured at roughly 3000
    /// a second on an ordinary working C:, one or two USN records each -- so anything carried out with
    /// the status is carried 3000 times a second to every subscriber, and no history small enough to
    /// send covers the window between two of them. Whoever cares about a particular directory says so
    /// once and hears back only when it is touched; see SearchRequestId.SubscribeDirectoryChanges.
    ///
    /// Raised on whichever thread applied the batch, outside LockObj, and must not block: the next
    /// batch is typically microseconds behind it.
    /// </remarks>
    public event Action<string, IReadOnlyCollection<string>?>? DirectoriesChanged;

    internal void RaiseDirectoriesChanged(string drive, IReadOnlyCollection<string>? changedDirectories)
    {
        try
        {
            DirectoriesChanged?.Invoke(drive, changedDirectories);
        }
        catch (Exception ex)
        {
            // A subscriber that throws must not take the indexer's apply loop down with it.
            Logger.Log($"[UsnIndexer] A directory-change subscriber threw: {ex.Message}", LogLevel.Error);
        }
    }
}
