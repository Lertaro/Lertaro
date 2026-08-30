using System.Collections.Concurrent;
using Lertaro.Plugins.ContentSearch.Extraction;
using Lertaro.Plugins.ContentSearch.Storage;

namespace Lertaro.Plugins.ContentSearch.Indexing;

/// <summary>
/// Extracts text for a batch of discovered files and routes each result to the database:
/// successful text is FTS-indexed, failures are recorded as failed rows, and files that
/// vanished or became excluded by configuration are deleted from the index.
/// Split out of ContentIndexScheduler purely to keep that file under the repository's
/// per-file line limit; this class holds no state of its own beyond the database reference.
/// </summary>
public sealed class IndexBatchProcessor
{
    private readonly ContentSearchDatabase _database;
    private readonly DuplicateContentResolver _duplicateResolver;

    public IndexBatchProcessor(ContentSearchDatabase database)
    {
        _database = database;
        _duplicateResolver = new DuplicateContentResolver(database);
    }

    public async Task ProcessBatchAsync(
        IReadOnlyList<string> filePaths,
        ContentIndexConfig config,
        CancellationToken ct)
    {
        var writeBatch = new ConcurrentBag<FileIndexBatchItem>();
        var failedBatch = new ConcurrentBag<FileIndexBatchItem>();
        var deleteBatch = new ConcurrentBag<string>();

        // Budget guard: past the configured index size cap, whole batches are skipped
        // (their deletions are re-evaluated on the next scan) until the user raises the
        // cap or clears the index. One warning per batch; identical repeats are
        // condensed by the logger.
        var indexBytes = _database.GetDatabasePageBytes();
        if (indexBytes > config.MaxIndexSizeBytes)
        {
            PluginSdk.Logger.Log(
                $"[ContentSearch] Index size cap reached ({indexBytes / (1024 * 1024)} MB of {config.MaxIndexSizeBytes / (1024 * 1024)} MB), skipping {filePaths.Count} file(s)",
                PluginSdk.LogLevel.Warn);
            return;
        }

        using var semaphore = new SemaphoreSlim(ContentIndexScheduler.GetExtractorParallelism(Environment.ProcessorCount));
        var tasks = filePaths.Select(async filePath =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                await ProcessSingleFileAsync(filePath, config, ct, writeBatch, failedBatch, deleteBatch);
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);

        WriteBatchesWithIntraBatchDedup(writeBatch, deleteBatch);

        if (!deleteBatch.IsEmpty)
            _database.DeleteFilesBatch(deleteBatch);

        if (!failedBatch.IsEmpty)
            _database.InsertOrUpdateBatch(failedBatch.ToList());
    }

    /// <summary>
    /// Writes successfully extracted items. Files processed in the same batch never see
    /// each other in the database, so duplicates of the same content are resolved here:
    /// the first item for a content hash stays the source row, the rest become duplicates
    /// referencing it once its row id is known.
    /// </summary>
    private void WriteBatchesWithIntraBatchDedup(ConcurrentBag<FileIndexBatchItem> writeBatch, ConcurrentBag<string> deleteBatch)
    {
        if (writeBatch.IsEmpty) return;

        var sources = new List<FileIndexBatchItem>(writeBatch.Count);
        var duplicates = new List<(FileIndexBatchItem Item, string SourcePath)>();
        var hashToSourcePath = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var item in writeBatch)
        {
            // Items that already reference a source (their DB lookup hit) are final.
            if (item.ContentHash is null || item.ContentRef is not null)
            {
                sources.Add(item);
                continue;
            }

            if (hashToSourcePath.TryGetValue(item.ContentHash, out var sourcePath))
            {
                // Keep the original content as the fallback for the (unreachable in
                // practice) case where the source row ends up missing from the write.
                duplicates.Add((item, sourcePath));
                continue;
            }

            hashToSourcePath[item.ContentHash] = item.Path;
            sources.Add(item);
        }

        var idByPath = _database.InsertOrUpdateBatch(sources);

        var resolvedDuplicates = duplicates
            .Select(d => idByPath.TryGetValue(d.SourcePath, out var sourceId)
                ? d.Item with { Content = string.Empty, ContentRef = sourceId }
                : d.Item) // degrade to a normal indexed row if the source is missing
            .ToList();

        if (resolvedDuplicates.Count > 0)
            _database.InsertOrUpdateBatch(resolvedDuplicates);
    }

    private async Task ProcessSingleFileAsync(
        string filePath,
        ContentIndexConfig config,
        CancellationToken ct,
        ConcurrentBag<FileIndexBatchItem> writeBatch,
        ConcurrentBag<FileIndexBatchItem> failedBatch,
        ConcurrentBag<string> deleteBatch)
    {
        try
        {
            if (ct.IsCancellationRequested || !ContentIndexScheduler.IsFileInMonitoredFolders(filePath, config) || !File.Exists(filePath))
            {
                deleteBatch.Add(filePath);
                return;
            }

            var fileInfo = new FileInfo(filePath);
            if (!config.IsAllowedExtension(filePath) || config.IsExcluded(filePath))
            {
                deleteBatch.Add(filePath);
                return;
            }

            // Oversized and empty files are kept as failed rows (not deleted) so an
            // unchanged file is not re-discovered and re-checked on every full scan.
            if (fileInfo.Length > config.MaxFileSizeBytes)
            {
                PluginSdk.Logger.Log(
                    $"[ContentSearch] '{filePath}' exceeds the configured max file size, skipped",
                    PluginSdk.LogLevel.Info);
                failedBatch.Add(MakeFailedItem(filePath, fileInfo));
                return;
            }

            // Large files are hashed before parsing: a duplicate of an already-indexed
            // document reuses the source row's text instead of paying for a second parse
            // and a second full copy of the text and FTS entry.
            var contentHash = DuplicateContentResolver.ComputeHashIfLarge(filePath, fileInfo.Length);
            if (_duplicateResolver.FindDuplicateSource(contentHash, filePath) is { } sourceId)
            {
                PluginSdk.Logger.Log(
                    $"[ContentSearch] '{filePath}' duplicates already-indexed content, reusing stored text",
                    PluginSdk.LogLevel.Info);
                writeBatch.Add(new FileIndexBatchItem(
                    filePath, fileInfo.LastWriteTimeUtc, fileInfo.Length, string.Empty, contentHash, sourceId));
                return;
            }

            var text = await TextExtractorRegistry.Instance.ExtractTextAsync(
                filePath, config.MaxFileSizeBytes, ct);

            if (text is null)
            {
                // The extractor already logged why (parse error, timeout, binary skip).
                // Record the failure so unchanged files are not re-extracted every scan.
                failedBatch.Add(MakeFailedItem(filePath, fileInfo));
                return;
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                PluginSdk.Logger.Log(
                    $"[ContentSearch] No extractable text{DescribeLikelyCause(filePath)}: '{filePath}'",
                    PluginSdk.LogLevel.Warn);
                failedBatch.Add(MakeFailedItem(filePath, fileInfo));
                return;
            }

            writeBatch.Add(new FileIndexBatchItem(filePath, fileInfo.LastWriteTimeUtc, fileInfo.Length, text, contentHash));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            PluginSdk.Logger.Log(
                $"[ContentSearch] Failed to index '{filePath}': {ex.Message}",
                PluginSdk.LogLevel.Warn);
        }
    }

    private static FileIndexBatchItem MakeFailedItem(string filePath, FileInfo fileInfo) =>
        new(filePath, fileInfo.LastWriteTimeUtc, fileInfo.Length, string.Empty);

    private static string DescribeLikelyCause(string filePath) =>
        filePath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)
            ? " (likely image-only PDF)"
            : string.Empty;
}
