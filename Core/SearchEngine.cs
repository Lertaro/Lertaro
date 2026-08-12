using Lertaro.Core.Indexer.Usn;

using Lertaro.Core.DriveMonitoring;

using Lertaro.Core.Services.Plugin.DirectoryIndex;
using Lertaro.Core.IndexV2.Space;
namespace Lertaro.Core;

public class SearchEngine : IDisposable
{
    private readonly UsnIndexer _indexer = new();
    private CancellationTokenSource? _cts;
    private readonly object _startLock = new();
    private bool _isRebuilding = false;
    private MachineSettings _machineSettings = MachineSettings.Load();
    private readonly SearchEngineDriveMaintenance _drives;

    // Search cancellation
    private CancellationTokenSource? _searchCts;
    private CancellationTokenSource? _searchDirCts;
    private readonly object _searchLock = new();
    private static readonly string IndexCacheDir = Path.Combine(Logger.SharedDataDir, "indexes");

    private long _lastDriveDetectTime = 0;
    private const long IdleTrimAfterMs = 3000;
    private readonly IdleTrimGate _idleTrim = new(IdleTrimAfterMs, Environment.TickCount64);
    private readonly Timer? _idleTimer;

    public SearchEngine()
    {
        _drives = new SearchEngineDriveMaintenance(
            _indexer,
            () => _machineSettings,
            () => _cts?.Token ?? CancellationToken.None,
            () => _isRebuilding,
            TryReleaseRuntimeAfterActivity);
        _idleTimer = new Timer(OnIdleTimerTick, null, IdleTrimAfterMs, IdleTrimAfterMs);
    }

    /// <summary>Every applied change batch: which drive, and where in it -- see UsnIndexer.</summary>
    public event Action<string, IReadOnlyCollection<string>?> DirectoriesChanged
    {
        add => _indexer.DirectoriesChanged += value;
        remove => _indexer.DirectoriesChanged -= value;
    }

    public event Action<UsnIndexer.IndexerStatus> StatusChanged
    {
        add => _indexer.StatusChanged += value;
        remove => _indexer.StatusChanged -= value;
    }

    private void OnIdleTimerTick(object? state)
    {
        if (!_idleTrim.ShouldTrim(Environment.TickCount64))
            return;

        Logger.Log("[SearchEngine] Service has been idle for 3s. Trimming working set...", LogLevel.Debug);
        _indexer.ClearCaches();
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        Win32Api.TrimWorkingSet();
    }

    public Dictionary<string, FileMetadataEntry> GetFileMetadataBatch(IReadOnlyList<string> paths) => _indexer.GetFileMetadataBatch(paths);

    public void ClearPathCaches() => _indexer.ClearAllPathCaches();

    public List<SearchResult> GetRecentFiles(IReadOnlyList<string> directories, int limit, int maxAgeMinutes) => _indexer.GetRecentFiles(directories, limit, maxAgeMinutes);
    public List<SpaceIndexEntry> GetSpaceEntries(string? directory) => _indexer.GetSpaceEntries(directory);

    public UsnIndexer.IndexerStatus GetStatus()
    {
        _indexer.Status.IsMaintenanceBusy = _isRebuilding || _drives.HasPendingRebuilds;
        var now = Environment.TickCount64;
        if (now - _lastDriveDetectTime > 5000 && (_indexer.Status.State is "ready" or "idle"))
        {
            _lastDriveDetectTime = now;
            RefreshDrivesInStatus();
        }
        return _drives.BuildStatusSnapshot();
    }

    private void RefreshDrivesInStatus()
        => _drives.RefreshDrivesInStatus();

    public bool RebuildDriveIndex(string drive) => _drives.RebuildDriveIndex(drive);

    public bool DeleteDriveIndex(string drive) => _drives.DeleteDriveIndex(drive);

    public bool CancelDriveIndex(string drive) => _drives.CancelDriveRebuild(drive);

    public MachineSettings GetMachineSettings() => _machineSettings;

    public void UpdateMachineSettings(MachineSettings settings)
    {
        var oldDrives = _machineSettings?.LocalDrives ?? new List<string>();
        var newDrives = settings.LocalDrives ?? new List<string>();

        var drivesChanged = !oldDrives.OrderBy(d => d).SequenceEqual(newDrives.OrderBy(d => d), StringComparer.OrdinalIgnoreCase);

        settings.LocalDriveSelectionConfigured = true;
        _machineSettings = settings;
        _machineSettings.Save();

        if (drivesChanged)
        {
            RefreshDrivesInStatus();
            _indexer.RaiseDirectoriesChanged(string.Empty, null);
        }
    }


    public bool SearchStreaming(
        string query,
        int fileLimit,
        int appLimit,
        string? directoryFilter,
        Action<SearchResult> onResult,
        CancellationToken requestToken = default)
    {
        // Marked in flight for the duration, and stamped again on the way out: this method blocks until
        // the whole search is done, so a query taking longer than the idle window would otherwise look
        // idle while it was still running. See IdleTrimGate for what that cost.
        _idleTrim.SearchStarted(Environment.TickCount64);
        try
        {
            return SearchStreamingCore(query, fileLimit, appLimit, directoryFilter, onResult, requestToken);
        }
        finally
        {
            _idleTrim.SearchFinished(Environment.TickCount64);
        }
    }

    private bool SearchStreamingCore(
        string query,
        int fileLimit,
        int appLimit,
        string? directoryFilter,
        Action<SearchResult> onResult,
        CancellationToken requestToken)
    {
        if (string.IsNullOrWhiteSpace(query))
            return true;

        CancellationTokenSource searchCts;
        lock (_searchLock)
        {
            if (string.IsNullOrEmpty(directoryFilter))
            {
                _searchCts?.Cancel();
                _searchCts = new CancellationTokenSource();
                searchCts = _searchCts;
            }
            else
            {
                _searchDirCts?.Cancel();
                _searchDirCts = new CancellationTokenSource();
                searchCts = _searchDirCts;
            }
        }

        // Deliberately no "is the index ready" check. There used to be one, on the single GLOBAL status
        // field, and it skipped the search outright for anything other than "ready" -- so rebuilding one
        // drive stopped every OTHER drive from being searched too, along with network and WSL sources
        // that have nothing to do with the local index at all. It reported success while doing it, so
        // the caller could not tell "no matches" from "never looked".
        //
        // Nothing was unavailable. A per-drive rebuild passes clearExisting: false
        // (SearchEngineDriveMaintenance.ForceRebuildDrive), and that flag is the only thing that clears
        // _recordIndexes -- so every drive's existing LiveIndex, including the one being rebuilt, stays
        // mapped and searchable for the whole scan, and the replacement is swapped in at the end. The
        // complete previous index was sitting right there the entire time.
        //
        // So the search simply runs over whatever indexes are currently loaded. SearchCoordinator fans
        // out across exactly those and no others, which degrades in the right direction on its own: a
        // drive is missing from the results only while it genuinely has no index -- the brief window
        // inside OnDriveCompleted where the old one is dropped before the new one is mapped, or a
        // from-scratch first build (clearExisting: true), which really does have nothing to offer yet.
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(searchCts.Token, requestToken);
        var searchToken = linkedCts.Token;

        _indexer.SearchStreaming(query, fileLimit, result =>
        {
            searchToken.ThrowIfCancellationRequested();
            onResult(result);
        }, searchToken, directoryFilter);

        return true;
    }

    // Directory listing straight off the index -- no query, no disk IO (see DirectoryEnumerator).
    // Deliberately outside the _searchCts/_searchDirCts cancellation pairs above: those exist so a new
    // keystroke supersedes the previous one's search, and an enumeration is not a keystroke -- two
    // plugins listing two different directories must not cancel each other. False = no loaded drive
    // index holds that path, so the caller has to walk the filesystem itself.
    public bool EnumerateDirectory(string path, bool recursive, string filterPattern, int limit, Action<SearchResult> onResult, CancellationToken token = default)
    {
        _idleTrim.SearchStarted(Environment.TickCount64);
        try
        {
            return _indexer.EnumerateDirectory(path, recursive, FilterPatternHelper.SplitOrNullIfMatchAll(filterPattern), limit, onResult, token);
        }
        finally
        {
            _idleTrim.SearchFinished(Environment.TickCount64);
        }
    }

    public void InitializeOrLoadIndex(bool forceRebuild = false)
    {
        lock (_startLock)
        {
            if (_isRebuilding) return;
            _isRebuilding = true;
        }
        lock (_indexer.LockObj)
        {
            _indexer.Status.State = forceRebuild ? "indexing" : "pending";
            _indexer.Status.Progress = 0;
        }
        _indexer.NotifyStatusChanged();

        Task.Run(() =>
        {
            // Cancel any active monitors
            _cts?.Cancel();
            _cts?.Dispose();
            _indexer.DisposeAllDriveMonitors();
            _cts = new CancellationTokenSource();

            var initializer = new SearchEngineInitializer(_indexer, IndexCacheDir, _drives.QueueDriveRebuild);
            initializer.Run(forceRebuild, _cts, isRebuilding =>
            {
                lock (_startLock)
                {
                    _isRebuilding = isRebuilding;
                }
                if (!isRebuilding)
                    TryReleaseRuntimeAfterActivity();
            });
        });
    }

    public void Dispose()
    {
        _idleTimer?.Dispose();
        _cts?.Cancel();
        _cts?.Dispose();
        _indexer.DisposeAllDriveMonitors();
        lock (_searchLock)
        {
            _searchCts?.Cancel();
            _searchCts?.Dispose();
            _searchDirCts?.Cancel();
            _searchDirCts?.Dispose();
        }
        GC.SuppressFinalize(this);
    }

    private void TryReleaseRuntimeAfterActivity()
    {
        if (_isRebuilding)
            return;

        _indexer.ClearCaches();
        Task.Run(async () =>
        {
            await Task.Delay(150);
            _indexer.CompactMemory();
        });
    }
}
