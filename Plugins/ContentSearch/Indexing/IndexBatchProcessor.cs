using System.Collections.Concurrent;
using Lertaro.Plugins.ContentSearch.Extraction;
using Lertaro.Plugins.ContentSearch.Storage;

namespace Lertaro.Plugins.ContentSearch.Indexing;

/// <summary>
/// Extracts text for a batch of discovered files and routes each result to the database:
/// successful text is FTS-indexed, failures are recorded as failed rows, and files that
/// vanished or became excluded by configuration are deleted from the index.
/// Split out of ContentIndexScheduler purely to keep that file under the repository's
/// per-file line limit; this class holds no state beyond the database reference and the
/// index-size pause monitor.
/// </summary>
public sealed class IndexBatchProcessor
{
    private readonly ContentSearchDatabase _database;
    private readonly DuplicateContentResolver _duplicateResolver;
    private readonly IndexCapPauseMonitor _capPause = new();

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
        var missingUpdates = new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        // Budget guard: past the configured index size cap nothing is extracted or written, but
        // the batch's deletions still are -- a row whose file vanished, moved out of scope or
        // became excluded is stale however full the index is, and holding it back would keep it
        // searchable until the user acts. The pause is reported once per episode, not once per
        // batch. Discovery re-enqueues every file whose row is absent or outdated, so the paths
        // this batch skipped are found again by a later full scan once the cap is raised or the
        // index cleared.
        var paused = _capPause.IsPaused(_database.GetDatabasePageBytes(), config.MaxIndexSizeBytes);

        using var semaphore = new SemaphoreSlim(ContentIndexScheduler.GetExtractorParallelism(Environment.ProcessorCount));
        var tasks = filePaths.Select(async filePath =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                await ProcessSingleFileAsync(filePath, config, ct, paused, writeBatch, failedBatch, deleteBatch, missingUpdates);
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);

        // A cancellation that raced past the per-file checks voids the whole batch:
        // nothing may reach the database, otherwise a shutdown would delete or rewrite
        // rows that are still valid and should survive until the next scan re-checks them.
        ct.ThrowIfCancellationRequested();

        if (paused)
        {
            // Deletions only: writing an extracted document while the cap is reached is exactly
            // what the cap forbids, and nothing was extracted to write anyway.
            if (!deleteBatch.IsEmpty)
                _database.DeleteFilesBatch(deleteBatch);

            if (!missingUpdates.IsEmpty)
                _database.UpdateMissingCounts(missingUpdates);

            return;
        }

        var writeFailures = DatabaseBatchWriteHelper.Write(writeBatch.ToList(), _database.InsertOrUpdateBatch);
        foreach (var failedItem in writeFailures)
            failedBatch.Add(failedItem);

        if (!deleteBatch.IsEmpty)
            _database.DeleteFilesBatch(deleteBatch);

        if (!missingUpdates.IsEmpty)
            _database.UpdateMissingCounts(missingUpdates);

        if (!failedBatch.IsEmpty)
            _database.InsertOrUpdateBatch(failedBatch.ToList());
    }

    private async Task ProcessSingleFileAsync(
        string filePath,
        ContentIndexConfig config,
        CancellationToken ct,
        bool paused,
        ConcurrentBag<FileIndexBatchItem> writeBatch,
        ConcurrentBag<FileIndexBatchItem> failedBatch,
        ConcurrentBag<string> deleteBatch,
        ConcurrentDictionary<string, int> missingUpdates)
    {
        try
        {
            // Cancellation must not turn into a delete: the file is still valid, so just
            // drop it from the batch. When every task has already cleared the semaphore
            // wait and none is mid-extraction, Task.WhenAll completes normally and a
            // deleteBatch entry here would delete a valid row on shutdown.
            if (ct.IsCancellationRequested) return;

            if (!ContentIndexScheduler.IsFileInMonitoredFolders(filePath, config))
            {
                // Out of scope by configuration: no later scan can make this row valid, so
                // it goes immediately (unlike a file that is merely unreachable right now).
                deleteBatch.Add(filePath);
                return;
            }

            if (!File.Exists(filePath))
            {
                ObserveMissingFile(filePath, deleteBatch, missingUpdates);
                return;
            }

            var fileInfo = new FileInfo(filePath);
            if (!config.IsAllowedExtension(filePath) || config.IsExcluded(filePath))
            {
                deleteBatch.Add(filePath);
                return;
            }

            // Paused at the size cap: the checks above still ran, because the deletions that
            // depend on them are applied even while the cap holds, but nothing may be extracted
            // or written.
            if (paused)
                return;

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

            // Hard per-file cap, covering both the dedup hash read and the extraction below: a
            // pathological file (e.g. a PDF whose parser never returns to a token check) or a
            // hash read stuck on a stalled share would otherwise block this lane forever and,
            // through Task.WhenAll, the whole batch. Waiting on the same budget guarantees the
            // batch moves on; abandoned work unblocks when its stream is disposed.
            var hardTimeout = ExtractorTimeoutPolicy.ForFileSize(fileInfo.Length).Add(TimeSpan.FromSeconds(5));
            using var hardTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            hardTimeoutCts.CancelAfter(hardTimeout);

            // Large files are hashed before parsing: a duplicate of an already-indexed
            // document reuses the source row's text instead of paying for a second parse
            // and a second full copy of the text and FTS entry. A hash that times out or is
            // cancelled yields no hash (logged once) rather than an exception.
            var contentHash = DuplicateContentResolver.ComputeHashIfLarge(filePath, fileInfo.Length, hardTimeoutCts.Token);
            if (_duplicateResolver.FindDuplicateSource(contentHash, filePath) is { } sourceId)
            {
                PluginSdk.Logger.Log(
                    $"[ContentSearch] '{filePath}' duplicates already-indexed content, reusing stored text",
                    PluginSdk.LogLevel.Info);
                writeBatch.Add(new FileIndexBatchItem(
                    filePath, fileInfo.LastWriteTimeUtc, fileInfo.Length, string.Empty, contentHash, sourceId));
                return;
            }

            string? text;
            try
            {
                text = await TextExtractorRegistry.Instance.ExtractTextAsync(
                    filePath, config.MaxFileSizeBytes, hardTimeoutCts.Token).WaitAsync(hardTimeout, ct);
            }
            catch (TimeoutException)
            {
                PluginSdk.Logger.Log(
                    $"[ContentSearch] Timed out extracting '{filePath}' after {hardTimeout.TotalSeconds:F0}s (hard cap)",
                    PluginSdk.LogLevel.Warn);
                failedBatch.Add(MakeFailedItem(filePath, fileInfo));
                return;
            }
            catch (OperationCanceledException) when (IsFileTimeout(hardTimeoutCts.Token, ct))
            {
                PluginSdk.Logger.Log(
                    $"[ContentSearch] Timed out extracting '{filePath}' after {hardTimeout.TotalSeconds:F0}s (hard cap)",
                    PluginSdk.LogLevel.Warn);
                failedBatch.Add(MakeFailedItem(filePath, fileInfo));
                return;
            }

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
            // Persist the failure too: otherwise the file has no row at all and every
            // full scan would re-discover and re-attempt it forever. MakeFailedItem is
            // best-effort here because the exception may have happened before metadata
            // could be read (e.g. File.Exists on a hung share).
            try
            {
                failedBatch.Add(MakeFailedItem(filePath, new FileInfo(filePath)));
            }
            catch
            {
                // Metadata unavailable; leave no row so the next scan retries it.
            }
        }
    }

    /// <summary>
    /// Records one missed observation for a path that discovery enqueued but that is not
    /// visible now. A stalled share makes File.Exists a momentary lie, so the row keeps its
    /// text until the shared retry limit is reached, exactly the grace the scan-time
    /// retention pass applies to files it did not discover.
    /// </summary>
    private void ObserveMissingFile(string filePath, ConcurrentBag<string> deleteBatch, ConcurrentDictionary<string, int> missingUpdates)
    {
        var (newCount, prune) = MissingObservationHelper.ObserveMiss(
            _database.GetFileRecord(filePath)?.MissingCount ?? 0);

        if (prune)
            deleteBatch.Add(filePath);
        else
            missingUpdates[filePath] = newCount;
    }

    internal static bool IsFileTimeout(CancellationToken fileTimeoutToken, CancellationToken batchToken) =>
        fileTimeoutToken.IsCancellationRequested && !batchToken.IsCancellationRequested;

    private static FileIndexBatchItem MakeFailedItem(string filePath, FileInfo fileInfo) =>
        new(filePath, fileInfo.LastWriteTimeUtc, fileInfo.Length, string.Empty);

    private static string DescribeLikelyCause(string filePath) =>
        filePath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)
            ? " (likely image-only PDF)"
            : string.Empty;
}
