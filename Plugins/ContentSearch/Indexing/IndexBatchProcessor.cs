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

    public IndexBatchProcessor(ContentSearchDatabase database) => _database = database;

    public async Task ProcessBatchAsync(
        IReadOnlyList<string> filePaths,
        ContentIndexConfig config,
        CancellationToken ct)
    {
        var writeBatch = new ConcurrentBag<FileIndexBatchItem>();
        var failedBatch = new ConcurrentBag<FileIndexBatchItem>();
        var deleteBatch = new ConcurrentBag<string>();

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

        if (!deleteBatch.IsEmpty)
            _database.DeleteFilesBatch(deleteBatch);

        if (!failedBatch.IsEmpty)
            _database.InsertOrUpdateBatch(failedBatch.ToList());

        if (!writeBatch.IsEmpty)
            _database.InsertOrUpdateBatch(writeBatch.ToList());
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
            if (!config.IsAllowedExtension(filePath))
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

            writeBatch.Add(new FileIndexBatchItem(filePath, fileInfo.LastWriteTimeUtc, fileInfo.Length, text));
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
