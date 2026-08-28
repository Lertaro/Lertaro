using System.Collections.Concurrent;
using Lertaro.PluginSdk.Services;
using Lertaro.Plugins.ContentSearch.Extraction;
using Lertaro.Plugins.ContentSearch.Storage;

namespace Lertaro.Plugins.ContentSearch.Indexing;

/// <summary>
/// Coordinates background indexing, file discovery, and incremental updates.
/// </summary>
public sealed class ContentIndexScheduler : IDisposable
{
    private const int MaxParallelExtractors = 4;
    private const int WriteBatchSize = 50;

    private readonly ContentSearchDatabase _database;
    private readonly ContentFolderWatcher _folderWatcher;
    private readonly ConcurrentQueue<string> _pendingFiles = new();
    private readonly HashSet<string> _enqueuedPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _queueLock = new();

    private CancellationTokenSource? _cts;
    private Task? _workerTask;
    private ContentIndexConfig _config = new();
    private bool _isScanning;

    public bool IsIndexing => _isScanning || !_pendingFiles.IsEmpty;
    public int PendingCount => _pendingFiles.Count;

    public ContentIndexScheduler(ContentSearchDatabase database)
    {
        _database = database;
        _folderWatcher = new ContentFolderWatcher(() => TriggerFullScan());
    }

    public void Start(ContentIndexConfig config)
    {
        UpdateConfig(config);
        _cts = new CancellationTokenSource();
        _workerTask = Task.Run(() => WorkerLoopAsync(_cts.Token));
        TriggerFullScan();
    }

    public void UpdateConfig(ContentIndexConfig config)
    {
        _config = config;
        _folderWatcher.UpdateFolders(config.MonitoredFolders);
    }

    public static string NormalizeFolderPath(string rawFolder)
    {
        var folder = Environment.ExpandEnvironmentVariables(rawFolder).Trim();
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

    public void TriggerFullScan() => Task.Run(async () =>
    {
        try
        {
            _isScanning = true;

            if (_config.MonitoredFolders.Count == 0)
            {
                _database.ClearAll();
                while (_pendingFiles.TryDequeue(out _)) { }
                lock (_queueLock) { _enqueuedPaths.Clear(); }
                return;
            }

            var existingMeta = _database.GetAllFileMetadata();
            var foundPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var rawFolder in _config.MonitoredFolders)
            {
                var folder = NormalizeFolderPath(rawFolder);
                if (string.IsNullOrEmpty(folder)) continue;

                try
                {
                    await foreach (var item in DirectoryIndexerService.EnumerateDirectoryAsync(folder, recursive: true))
                    {
                        if (item.IsDir) continue;
                        var file = item.FullPath;
                        var ext = Path.GetExtension(file);
                        if (!_config.AllowedExtensions.Contains(ext) ||
                            !TextExtractorRegistry.Instance.IsSupportedExtension(ext))
                            continue;

                        foundPaths.Add(file);

                        var fileSize = item.Metadata.Size;
                        var modified = item.Metadata.Modified;
                        if (fileSize == 0 && modified == default)
                        {
                            try
                            {
                                var fi = new FileInfo(file);
                                fileSize = fi.Length;
                                modified = fi.LastWriteTimeUtc;
                            }
                            catch { }
                        }

                        if (fileSize == 0 || fileSize > _config.MaxFileSizeBytes)
                            continue;

                        var lastModUnix = new DateTimeOffset(modified).ToUnixTimeSeconds();
                        if (existingMeta.TryGetValue(file, out var meta) &&
                            meta.LastModified == lastModUnix &&
                            meta.FileSize == fileSize)
                        {
                            continue;
                        }

                        QueueFileChange(file);
                    }
                }
                catch { }
            }

            var toDelete = new List<string>();
            foreach (var dbPath in existingMeta.Keys)
            {
                if (!foundPaths.Contains(dbPath))
                {
                    toDelete.Add(dbPath);
                }
            }

            if (toDelete.Count > 0)
            {
                _database.DeleteFilesBatch(toDelete);
            }
        }
        finally
        {
            _isScanning = false;
        }
    });

    public void QueueFileChange(string filePath)
    {
        var ext = Path.GetExtension(filePath);
        if (!_config.AllowedExtensions.Contains(ext) ||
            !TextExtractorRegistry.Instance.IsSupportedExtension(ext))
            return;

        if (!IsFileInMonitoredFolders(filePath))
            return;

        lock (_queueLock)
        {
            if (_enqueuedPaths.Add(filePath))
                _pendingFiles.Enqueue(filePath);
        }
    }

    public bool IsFileInMonitoredFolders(string filePath)
    {
        try
        {
            var fullPath = Path.GetFullPath(filePath);
            foreach (var rawFolder in _config.MonitoredFolders)
            {
                var folder = NormalizeFolderPath(rawFolder);
                if (string.IsNullOrEmpty(folder)) continue;

                var prefix = folder.EndsWith(Path.DirectorySeparatorChar) || folder.EndsWith(Path.AltDirectorySeparatorChar)
                    ? folder
                    : folder + Path.DirectorySeparatorChar;

                if (fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(fullPath, folder, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        catch { }

        return false;
    }

    private async Task WorkerLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var batch = new List<string>();
            while (batch.Count < WriteBatchSize && _pendingFiles.TryDequeue(out var path))
            {
                lock (_queueLock) { _enqueuedPaths.Remove(path); }
                batch.Add(path);
            }

            if (batch.Count == 0)
            {
                await Task.Delay(200, ct);
                continue;
            }

            await ProcessBatchAsync(batch, ct);
        }
    }

    private async Task ProcessBatchAsync(IReadOnlyList<string> filePaths, CancellationToken ct)
    {
        var writeBatch = new ConcurrentBag<FileIndexBatchItem>();
        var deleteBatch = new ConcurrentBag<string>();

        using var semaphore = new SemaphoreSlim(MaxParallelExtractors);
        var tasks = filePaths.Select(async filePath =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                if (!IsFileInMonitoredFolders(filePath))
                {
                    deleteBatch.Add(filePath);
                    return;
                }

                if (!File.Exists(filePath))
                {
                    deleteBatch.Add(filePath);
                    return;
                }

                var fileInfo = new FileInfo(filePath);
                var ext = fileInfo.Extension;
                if (!_config.AllowedExtensions.Contains(ext) ||
                    !TextExtractorRegistry.Instance.IsSupportedExtension(ext) ||
                    fileInfo.Length > _config.MaxFileSizeBytes ||
                    fileInfo.Length == 0)
                    return;

                var text = await TextExtractorRegistry.Instance.ExtractTextAsync(
                    filePath, _config.MaxFileSizeBytes, ct);

                if (string.IsNullOrWhiteSpace(text))
                {
                    deleteBatch.Add(filePath);
                    return;
                }

                var chunks = TextChunker.ChunkText(text);
                if (chunks.Count > 0)
                    writeBatch.Add(new FileIndexBatchItem(filePath, fileInfo.LastWriteTimeUtc, fileInfo.Length, chunks));
            }
            catch
            {
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);

        if (!deleteBatch.IsEmpty)
            _database.DeleteFilesBatch(deleteBatch);

        if (!writeBatch.IsEmpty)
            _database.InsertOrUpdateBatch(writeBatch.ToList());
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _folderWatcher.Dispose();
        try { _workerTask?.Wait(1000); } catch { }
        _cts?.Dispose();
    }
}
