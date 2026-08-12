using Lertaro.Core.Indexer.NetworkDrive.Scheduling;
namespace Lertaro.Core.Indexer.NetworkDrive;

public sealed class NetworkIndexer : IDisposable
{
    public event Action<IReadOnlyList<NetworkIndexStatus>>? StatusesChanged;

    /// <summary>
    /// A network, WSL or folder index took content in, and where -- null directories meaning a whole
    /// tree was replaced. The in-process counterpart of UsnIndexer.DirectoriesChanged.
    /// </summary>
    public event Action<string, IReadOnlyCollection<string>?>? DirectoriesChanged;

    private readonly object _gate = new();
    internal object Gate => _gate;
    internal readonly Dictionary<string, NetworkIndex> _indexes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, NetworkIndexStatus> _statuses = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _refreshModes = new(StringComparer.OrdinalIgnoreCase);
    private bool _configured;

    private WatcherManager? _watcherManager;
    private Scheduler? _scheduler;
    private readonly NetworkIndexerPublisher _publisher;

    public NetworkIndexer()
    {
        _publisher = new NetworkIndexerPublisher(
            _gate, _statuses, _indexes,
            drive => _watcherManager?.EnsureWatcher(drive),
            GetStatuses,
            statuses => StatusesChanged?.Invoke(statuses),
            (drive, reason) => _scheduler?.QueueRefreshDrive(drive, reason),
            NotifyDirectoriesChanged);

        _watcherManager = new WatcherManager(
            (drive, reason) => _scheduler?.QueueRefreshDrive(drive, reason),
            drive => { lock (_gate) { _indexes.TryGetValue(drive, out var idx); return idx; } },
            (drive, idx, changedDirectories) => _publisher.PublishIncrementalUpdate(drive, idx, changedDirectories),
            drive => _publisher.MarkMissedIfRescanning(drive)
        );

        _scheduler = new Scheduler(
            (drive, mode) => _watcherManager?.EnsureWatcher(drive),
            drive => _watcherManager?.RemoveWatcher(drive),
            _publisher.SetStatus,
            _publisher.OnRefreshFinished,
            _publisher.PublishCheckpoint,
            _publisher.GetPreviousStore,
            drive => _publisher.ReleaseCachedIndex(drive)
        );
    }

    public void EnsureConfigured()
    {
        if (_configured)
            return;

        lock (_gate)
        {
            if (_configured)
                return;

            var settings = UserSettings.Load();
            _configured = true;
            try
            {
                Configure(settings.NetworkDrives, settings.WslSettings, settings.FolderIndexes);
            }
            catch
            {
                _configured = false;
                throw;
            }
        }
    }

    public void Configure(
        IEnumerable<NetworkDriveSetting> driveSettings,
        IEnumerable<WslSetting> wslSettings,
        IEnumerable<FolderIndexSetting>? folderSettings = null,
        bool forceRefresh = false)
    {
        var wslPrefix = @"\\wsl$";
        var enabledSettings = driveSettings
            .Select(d => new
            {
                Drive = NetworkIndexerHelper.ResolveDriveFromId(d.Id),
                RefreshMode = IndexerHelper.NormalizeRefreshMode(d.RefreshMode)
            })
            .Where(d => d.Drive.Length == 1)
            .Concat(wslSettings.Select(w => new
            {
                Drive = $@"{wslPrefix}\{w.Id}",
                RefreshMode = IndexerHelper.NormalizeRefreshMode(w.RefreshMode)
            }))
            // A folder-index path is already absolute -- normalized (but not collapsed to a letter, see
            // IndexerHelper.NormalizeDrive), it becomes its own opaque key, same as a WSL UNC path.
            .Concat((folderSettings ?? Enumerable.Empty<FolderIndexSetting>()).Select(f => new
            {
                Drive = IndexerHelper.NormalizeDrive(f.Path),
                RefreshMode = IndexerHelper.NormalizeRefreshMode(f.RefreshMode)
            }))
            .Where(d => !string.IsNullOrEmpty(d.Drive))
            .GroupBy(d => d.Drive, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
        var enabledDrives = enabledSettings.Select(d => d.Drive).ToList();
        var refreshModes = enabledSettings.ToDictionary(d => d.Drive, d => d.RefreshMode, StringComparer.OrdinalIgnoreCase);

        var cachedDrives = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var lastUpdatedTimes = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        var removedIndexes = new List<NetworkIndex>();
        List<string> changedRoots;

        lock (_gate)
        {
            changedRoots = NetworkIndexerHelper.FindChangedRoots(_refreshModes.Keys, enabledDrives);
            foreach (var removed in _indexes.Keys.Except(enabledDrives, StringComparer.OrdinalIgnoreCase).ToList())
            {
                if (_indexes.Remove(removed, out var removedIndex))
                    removedIndexes.Add(removedIndex);
                _statuses.Remove(removed);
                _refreshModes.Remove(removed);
            }

            foreach (var drive in enabledDrives)
            {
                _refreshModes[drive] = refreshModes[drive];
                if (!_statuses.ContainsKey(drive))
                {
                    _statuses[drive] = new NetworkIndexStatus
                    {
                        Drive = drive,
                        State = "pending",
                        CachePath = IndexerHelper.GetCachePath(drive)
                    };
                }

                if (!_indexes.ContainsKey(drive))
                {
                    if (IndexerHelper.TryLoad(drive, out var index))
                    {
                        _indexes[drive] = index;
                        _statuses[drive] = NetworkIndexerHelper.CreateStatus(drive, "cached", index.Count, index, null);
                        // An incomplete cache (interrupted scan) must not be mistaken for "nothing to do" --
                        // only a fully-finished index skips the initial refresh below.
                        if (index.IsComplete)
                            cachedDrives.Add(drive);
                        lastUpdatedTimes[drive] = index.LastUpdated;
                    }
                }
                else
                {
                    if (_indexes[drive].IsComplete)
                        cachedDrives.Add(drive);
                    lastUpdatedTimes[drive] = _indexes[drive].LastUpdated;
                }
            }
        }

        // Disposed outside the lock -- see OnRefreshFinished's comment on why (LiveIndex.Dispose() can
        // briefly block on an in-flight search's read lock).
        foreach (var removedIndex in removedIndexes)
            removedIndex.Dispose();

        _scheduler?.StartRefresh(enabledDrives, refreshModes, forceRefresh ? null : cachedDrives, forceRefresh ? null : lastUpdatedTimes);
        _publisher.PublishStatusesChanged();
        foreach (var root in changedRoots)
            NotifyDirectoriesChanged(root, null);
    }

    public bool RefreshDrive(string drive)
    {
        EnsureConfigured();
        drive = IndexerHelper.NormalizeDrive(drive);
        if (drive.Length == 0)
            return false;

        lock (_gate)
        {
            if (!_refreshModes.ContainsKey(drive))
                return false;
            if (_statuses.Values.Any(s => s.State is "indexing" or "pending"))
                return false;
        }

        _publisher.SetStatus(drive, "indexing", 0, null);
        _scheduler?.QueueRefreshDrive(drive, "manual");
        return true;
    }

    public bool CancelDrive(string drive)
    {
        drive = IndexerHelper.NormalizeDrive(drive);
        if (drive.Length == 0)
            return false;

        lock (_gate)
        {
            if (!_refreshModes.ContainsKey(drive))
                return false;
        }

        _scheduler?.CancelDrive(drive);
        return true;
    }

    public IReadOnlyList<NetworkIndexStatus> GetStatuses()
    {
        EnsureConfigured();
        lock (_gate)
            return _statuses.Values.Select(s => s.Clone()).OrderBy(s => s.Drive).ToList();
    }

    public void DeleteCache(string drive)
    {
        drive = IndexerHelper.NormalizeDrive(drive);
        if (drive.Length == 0)
            return;

        IndexerHelper.DeleteCache(drive);
        NetworkIndex? removedIndex;
        lock (_gate)
        {
            _indexes.Remove(drive, out removedIndex);
            _statuses.Remove(drive);
        }
        removedIndex?.Dispose();
        _publisher.PublishStatusesChanged();
        NotifyDirectoriesChanged(drive, null);
    }

    // Called whenever a search window closes/hides (mirrors ShellIconHelper.ClearCache()'s existing
    // trigger points and UsnIndexer.ClearAllPathCaches on the local-drive side). Unlike the local side,
    // network/WSL/folder-index drives have no idle-timer-driven cache trim, so this also has to cover
    // NetworkIndex.ClearCaches (candidate/rank cache), not just the path memo.
    public void ClearAllCaches()
    {
        NetworkIndex[] snapshots;
        lock (_gate)
            snapshots = _indexes.Values.ToArray();

        foreach (var index in snapshots)
        {
            index.ClearPathCache();
            index.ClearCaches();
        }
    }

    private void NotifyDirectoriesChanged(string drive, IReadOnlyCollection<string>? changedDirectories)
    {
        try { DirectoriesChanged?.Invoke(drive, changedDirectories); }
        catch (Exception ex) { Logger.Log($"[NetworkIndexer] A directory-change subscriber threw: {ex.Message}", LogLevel.Error); }
    }

    public void Dispose()
    {
        _scheduler?.Dispose();
        _scheduler = null;

        _watcherManager?.Dispose();
        _watcherManager = null;

        lock (_gate)
        {
            foreach (var index in _indexes.Values)
                index.Dispose();
            _indexes.Clear();
        }
    }
}
