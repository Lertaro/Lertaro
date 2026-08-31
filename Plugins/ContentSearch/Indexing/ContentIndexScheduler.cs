using System.Collections.Concurrent;
using System.Diagnostics;
using Lertaro.PluginSdk.Helpers;
using Lertaro.Plugins.ContentSearch.Storage;

namespace Lertaro.Plugins.ContentSearch.Indexing;

/// <summary>
/// Coordinates background indexing, file discovery, and incremental updates.
/// </summary>
// ponytail: this file sits slightly above the repo's 300-line guideline on purpose. The only
// mechanical split (moving NormalizeFolderPath/IsFileInMonitoredFolders into a path helper)
// would save ~35 lines but add a low-cohesion helper and touch five call sites for little
// readability gain; if it grows further, extract the worker/scan loop instead.
public sealed class ContentIndexScheduler : IDisposable
{
    private const int WriteBatchSize = 50;

    private readonly ContentSearchDatabase _database;
    private readonly ContentFolderWatcher _folderWatcher;
    private readonly IndexBatchProcessor _batchProcessor;
    private readonly ConcurrentQueue<string> _pendingFiles = new();
    private readonly HashSet<string> _enqueuedPaths = new(StringComparer.OrdinalIgnoreCase);
    // Files the worker has dequeued and is currently extracting/writing. A watcher-triggered
    // full scan racing that in-flight batch must not re-enqueue them (their rows are not in
    // the database yet), otherwise the same file is extracted twice and progress jumps.
    private readonly HashSet<string> _inFlightPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _queueLock = new();
    private readonly SemaphoreSlim _scanGate = new(1, 1);

    private CancellationTokenSource? _cts;
    private CancellationTokenSource? _scanCts;
    private Task? _workerTask;
    private volatile ContentIndexConfig _config = new();

    public bool IsIndexing => !_pendingFiles.IsEmpty;
    public int PendingCount => _pendingFiles.Count;

    // Raised from the scheduler's background thread whenever the visible indexing state
    // (queued count, committed rows) changes. Subscribers must marshal to their own
    // thread; the host wires this to SearchRefreshService to refresh a visible "cs "
    // placeholder in place.
    public event Action? ProgressChanged;

    // Extraction is CPU-heavy (PDF/XML parsing). Running all four lanes on a low-core machine
    // starves the UI thread and thread-pool continuations. The settings window then takes
    // seconds to open while indexing. Cap the lanes at half the cores so the UI always keeps
    // headroom, with 4 as the ceiling for high-core machines.
    internal static int GetExtractorParallelism(int processorCount) =>
        Math.Clamp(processorCount / 2, 1, 4);

    public ContentIndexScheduler(ContentSearchDatabase database)
    {
        _database = database;
        _batchProcessor = new IndexBatchProcessor(database);
        _folderWatcher = new ContentFolderWatcher(() => TriggerFullScan());
    }

    public void Start(ContentIndexConfig config)
    {
        UpdateConfig(config);
        _cts = new CancellationTokenSource();
        _workerTask = Task.Factory.StartNew(
            () =>
            {
                try
                {
                    // Dedicated below-normal thread: the scheduler loop and DB writes must
                    // never win CPU against the UI. LongRunning keeps this thread out of the
                    // thread pool, so its priority sticks and pool starvation cannot stall it.
                    Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;
                    WorkerLoop(_cts.Token);
                }
                catch (OperationCanceledException) { }
            },
            _cts.Token,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
        TriggerFullScan();
    }

    public void UpdateConfig(ContentIndexConfig config)
    {
        _config = config;
        _folderWatcher.UpdateFolders(config.MonitoredFolders, config.FilterPattern);
    }

    public static string NormalizeFolderPath(string rawFolder)
    {
        var folder = Environment.ExpandEnvironmentVariables(rawFolder).Trim();
        if (string.IsNullOrWhiteSpace(folder)) return string.Empty;

        // Whitelist entries may use Windows shell virtual paths (e.g. "shell:Personal"); these
        // must be resolved to their physical folder before any filesystem/Path operations,
        // which would otherwise fail or mangle the "shell:" prefix.
        folder = ShellPathHelper.TryResolveVirtualPath(folder);
        if (string.IsNullOrWhiteSpace(folder)) return string.Empty;

        if (folder.Length == 2 && char.IsLetter(folder[0]) && folder[1] == ':')
            folder += @"\";

        try
        {
            folder = Path.GetFullPath(folder);
            return Path.TrimEndingDirectorySeparator(folder);
        }
        catch
        {
            return folder;
        }
    }

    public void TriggerFullScan()
    {
        var newCts = new CancellationTokenSource();
        var oldCts = Interlocked.Exchange(ref _scanCts, newCts);
        oldCts?.Cancel();
        oldCts?.Dispose();

        // Deliberately do NOT clear _pendingFiles/_enqueuedPaths here: the pending work is
        // still valid, and clearing it is what let a watcher-triggered scan re-enqueue the
        // same files (and made the "remaining" progress count jump). The worker still picks
        // up config changes per file via ProcessSingleFileAsync.

        var ct = newCts.Token;
        Task.Run(async () =>
        {
            try { await _scanGate.WaitAsync(ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }

            try
            {
                if (ct.IsCancellationRequested) return;

                if (_config.MonitoredFolders.Count == 0 || _config.AllowedExtensions.Count == 0)
                {
                    _database.ClearAll();
                    return;
                }

                var scanStopwatch = Stopwatch.StartNew();

                var existingMeta = _database.GetAllFileMetadata();
                // Deliberately no size check here: lowering MaxFileSizeBytes does not
                // prune already-indexed oversized rows, they keep serving their stale
                // text until a full index rebuild (see FolderScanDiscoveryHelper for the
                // enqueue side of the same trade-off).
                var toDeleteImmediately = existingMeta.Keys
                    .Where(p => !IsFileInMonitoredFolders(p) || !IsAllowedExtension(p) || _config.IsExcluded(p))
                    .ToList();

                if (toDeleteImmediately.Count > 0)
                {
                    _database.DeleteFilesBatch(toDeleteImmediately);
                    foreach (var p in toDeleteImmediately)
                        existingMeta.Remove(p);
                }

                var discovered = await FolderScanDiscoveryHelper.DiscoverFilesAsync(
                    _config,
                    existingMeta,
                    EnqueueFile,
                    ct).ConfigureAwait(false);

                if (ct.IsCancellationRequested) return;

                // A monitored NAS/share can be offline while the app keeps running. A scan
                // while it is unreachable must not prune every file below it as "vanished":
                // only prune paths whose monitored folder is currently reachable, so the
                // offline share's content remains searchable from the local index.
                var reachableFolders = _config.MonitoredFolders
                    .Select(NormalizeFolderPath)
                    .Where(folder => !string.IsNullOrEmpty(folder) && Directory.Exists(folder))
                    .ToList();
                var reachableConfig = new ContentIndexConfig
                {
                    MonitoredFolders = reachableFolders,
                    AllowedExtensions = _config.AllowedExtensions,
                    ExcludedPatterns = _config.ExcludedPatterns
                };

                var missingResult = MissingObservationHelper.ApplyRetention(
                    _database,
                    existingMeta,
                    discovered,
                    reachableConfig);

                ProgressChanged?.Invoke();

                _database.Optimize();
                _database.VacuumIfBloat();

                scanStopwatch.Stop();
                // One info line per scan, only when there is actual indexing work: it tells the
                // user how many files this scan queued without the folder-count noise or the
                // per-scan completion bookkeeping that used to be logged here.
                if (PendingCount > 0)
                {
                    PluginSdk.Logger.Log(
                        $"[ContentSearch] Started scanning {PendingCount} file(s)",
                        PluginSdk.LogLevel.Info);
                }
            }
            catch (Exception ex)
            {
                PluginSdk.Logger.Log(
                    $"[ContentSearch] Full scan failed: {ex.Message}", PluginSdk.LogLevel.Error);
            }
            finally
            {
                _scanGate.Release();
            }
        }, ct);
    }

    public void EnqueueFile(string filePath)
    {
        lock (_queueLock)
        {
            if (_inFlightPaths.Contains(filePath) || !_enqueuedPaths.Add(filePath))
            {
                return;
            }

            _pendingFiles.Enqueue(filePath);
        }
    }

    internal bool IsFileInMonitoredFolders(string filePath) =>
        IsFileInMonitoredFolders(filePath, _config);

    internal static bool IsFileInMonitoredFolders(string filePath, ContentIndexConfig config)
    {
        foreach (var rawFolder in config.MonitoredFolders)
        {
            var folder = NormalizeFolderPath(rawFolder);
            if (string.IsNullOrEmpty(folder)) continue;

            var folderWithSep = folder.EndsWith('\\') || folder.EndsWith('/') ? folder : folder + Path.DirectorySeparatorChar;
            if (filePath.StartsWith(folderWithSep, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    internal bool IsAllowedExtension(string filePath) => _config.IsAllowedExtension(filePath);

    private void WorkerLoop(CancellationToken ct)
    {
        var hasPendingOptimizations = false;
        var idleCycles = 0;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var batch = new List<string>();
                while (batch.Count < WriteBatchSize && _pendingFiles.TryDequeue(out var path))
                {
                    lock (_queueLock) { _enqueuedPaths.Remove(path); _inFlightPaths.Add(path); }
                    batch.Add(path);
                }

                if (batch.Count == 0)
                {
                    if (hasPendingOptimizations)
                    {
                        idleCycles++;
                        if (idleCycles >= 15) // ~3 seconds of idle time
                        {
                            _database.Optimize();
                            hasPendingOptimizations = false;
                            idleCycles = 0;
                        }
                    }
                    if (ct.WaitHandle.WaitOne(200)) break;
                    continue;
                }

                idleCycles = 0;
                hasPendingOptimizations = true;

                try
                {
                    // Blocking wait is fine here: this is the dedicated below-normal scheduler
                    // thread, and the parallel extraction lanes run on thread-pool threads.
                    _batchProcessor.ProcessBatchAsync(batch, _config, ct).GetAwaiter().GetResult();
                    _database.Checkpoint(truncate: false);
                }
                finally
                {
                    lock (_queueLock)
                    {
                        foreach (var path in batch)
                            _inFlightPaths.Remove(path);
                    }
                }

                ProgressChanged?.Invoke();
                if (ct.WaitHandle.WaitOne(20)) break;
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                // A transient failure (SQLite I/O error, disk full) must not kill the
                // worker thread permanently until app restart: log it, drop the failed
                // batch (the next full scan re-enqueues those files) and keep serving
                // later batches.
                PluginSdk.Logger.Log(
                    $"[ContentSearch] Indexing batch failed, the next full scan retries it: {ex.Message}",
                    PluginSdk.LogLevel.Error);
            }
        }
    }

    public void Dispose()
    {
        _scanCts?.Cancel();
        _scanCts?.Dispose();
        _cts?.Cancel();
        _folderWatcher.Dispose();
        try { _workerTask?.Wait(1000); } catch { }
        _cts?.Dispose();
    }
}
