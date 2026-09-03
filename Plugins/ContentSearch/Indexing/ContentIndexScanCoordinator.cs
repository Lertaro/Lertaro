using Lertaro.Plugins.ContentSearch.Storage;

namespace Lertaro.Plugins.ContentSearch.Indexing;

/// <summary>
/// Runs serialized full scans for one scheduler. Split out to keep the scheduler below the
/// repository per-file line limit; this class owns only scan lifecycle state for its scheduler.
/// </summary>
internal sealed class ContentIndexScanCoordinator : IDisposable
{
    private readonly ContentIndexScheduler _scheduler;
    private readonly ContentSearchDatabase _database;
    private readonly SemaphoreSlim _scanGate = new(1, 1);
    private CancellationTokenSource? _scanCts;

    public ContentIndexScanCoordinator(ContentIndexScheduler scheduler, ContentSearchDatabase database)
    {
        _scheduler = scheduler;
        _database = database;
    }

    public void TriggerFullScan()
    {
        var newCts = new CancellationTokenSource();
        var oldCts = Interlocked.Exchange(ref _scanCts, newCts);
        oldCts?.Cancel();
        oldCts?.Dispose();

        // Deliberately do NOT clear pending files here: the work remains valid, and clearing it
        // lets a watcher-triggered scan enqueue the same files a second time.
        var ct = newCts.Token;
        Task.Run(async () =>
        {
            try { await _scanGate.WaitAsync(ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }

            try
            {
                if (ct.IsCancellationRequested) return;
                var config = _scheduler.CurrentConfig;
                if (config.MonitoredFolders.Count == 0 || config.AllowedExtensions.Count == 0)
                {
                    _database.ClearAll();
                    _scheduler.NotifyProgressChanged(force: true);
                    return;
                }

                var existingMeta = _database.GetAllFileMetadata();
                var toDeleteImmediately = existingMeta.Keys
                    .Where(p => !_scheduler.IsFileInMonitoredFolders(p) ||
                                !_scheduler.IsAllowedExtension(p) || config.IsExcluded(p))
                    .ToList();

                if (toDeleteImmediately.Count > 0)
                {
                    _database.DeleteFilesBatch(toDeleteImmediately);
                    foreach (var p in toDeleteImmediately)
                        existingMeta.Remove(p);
                }

                var discovered = await FolderScanDiscoveryHelper.DiscoverFilesAsync(
                    config,
                    existingMeta,
                    _scheduler.EnqueueFile,
                    ct).ConfigureAwait(false);

                if (ct.IsCancellationRequested) return;

                // An offline NAS/share must not have all its rows pruned as vanished. Only
                // reachable monitored folders participate in missing-file retention.
                var reachableFolders = config.MonitoredFolders
                    .Select(ContentIndexScheduler.NormalizeFolderPath)
                    .Where(folder => !string.IsNullOrEmpty(folder) && Directory.Exists(folder))
                    .ToList();
                var reachableConfig = new ContentIndexConfig
                {
                    MonitoredFolders = reachableFolders,
                    AllowedExtensions = config.AllowedExtensions,
                    ExcludedPatterns = config.ExcludedPatterns
                };

                MissingObservationHelper.ApplyRetention(
                    _database,
                    existingMeta,
                    discovered,
                    reachableConfig);

                _scheduler.NotifyProgressChanged(force: _scheduler.PendingCount == 0);
                _database.Optimize();
                _database.VacuumIfBloat();

                if (_scheduler.PendingCount > 0)
                {
                    PluginSdk.Logger.Log(
                        $"[ContentSearch] Started scanning {_scheduler.PendingCount} file(s)",
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

    public void CancelPendingScan()
    {
        var scanCts = Interlocked.Exchange(ref _scanCts, null);
        scanCts?.Cancel();
        scanCts?.Dispose();
    }

    public void Dispose() => CancelPendingScan();
}
