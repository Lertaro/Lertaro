using System.Collections.Concurrent;
using Lertaro.PluginSdk.Helpers;
using Lertaro.Plugins.ContentSearch.Storage;

namespace Lertaro.Plugins.ContentSearch.Indexing;

/// <summary>
/// Coordinates background indexing, file discovery, and incremental updates.
/// </summary>
public sealed class ContentIndexScheduler : IDisposable
{
    private const int WriteBatchSize = 50;
    private const int ProgressNotifyIntervalMs = 3000;

    private readonly ContentSearchDatabase _database;
    private readonly ContentFolderWatcher _folderWatcher;
    private readonly IndexBatchProcessor _batchProcessor;
    private readonly ContentIndexScanCoordinator _scanCoordinator;
    private readonly ConcurrentQueue<string> _pendingFiles = new();
    private readonly HashSet<string> _enqueuedPaths = new(StringComparer.OrdinalIgnoreCase);
    // Files the worker has dequeued and is currently extracting/writing. A watcher-triggered
    // full scan racing that in-flight batch must not re-enqueue them (their rows are not in
    // the database yet), otherwise the same file is extracted twice and progress jumps.
    private readonly HashSet<string> _inFlightPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _queueLock = new();
    private readonly SemaphoreSlim _scanGate = new(1, 1);

    private CancellationTokenSource? _cts;
    private Task? _workerTask;
    private long _lastProgressNotifyTick;
    private volatile ContentIndexConfig _config = new();

    public bool IsIndexing => !_pendingFiles.IsEmpty;
    public int PendingCount => _pendingFiles.Count;
    internal ContentIndexConfig CurrentConfig => _config;

    // Raised from the scheduler's background thread when the visible indexing state
    // (queued count, committed rows) changes, throttled to ProgressNotifyIntervalMs
    // except for the final transition to idle. Subscribers must marshal to their own
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
        _scanCoordinator = new ContentIndexScanCoordinator(this, database);
        // Let the first progress notification go out immediately; later ones are
        // throttled to ProgressNotifyIntervalMs so indexing batches do not flood
        // the UI dispatcher with placeholder refresh requests.
        _lastProgressNotifyTick = Environment.TickCount64 - ProgressNotifyIntervalMs;
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
        => _scanCoordinator.TriggerFullScan();

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

                NotifyProgressChanged(force: _pendingFiles.IsEmpty);
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

    internal void NotifyProgressChanged(bool force)
    {
        var now = Environment.TickCount64;
        if (!force && now - _lastProgressNotifyTick < ProgressNotifyIntervalMs)
            return;

        _lastProgressNotifyTick = now;
        ProgressChanged?.Invoke();
    }

    public void Dispose()
    {
        _scanCoordinator.Dispose();
        _cts?.Cancel();
        _folderWatcher.Dispose();
        try { _workerTask?.Wait(1000); } catch { }
        _cts?.Dispose();
    }
}
