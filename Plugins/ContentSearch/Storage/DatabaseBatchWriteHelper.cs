namespace Lertaro.Plugins.ContentSearch.Storage;

/// <summary>
/// Writes a processed batch of file rows: resolves duplicates that share a content hash inside
/// the batch, and isolates the items the database refuses so one unwritable file cannot discard
/// the healthy files of its batch.
/// The normal path is untouched: a batch that writes cleanly is still one atomic write. Only a
/// failed write is retried in halves, until the item at fault stands alone.
/// Split out of IndexBatchProcessor purely to keep that file under the repository's per-file
/// line limit; this class has no state of its own, it always operates on the batch it is handed.
/// </summary>
public static class DatabaseBatchWriteHelper
{
    /// <summary>
    /// Writes the batch and returns the items that could not be written, with their text and any
    /// duplicate reference cleared: that is the shape every other extraction failure takes, so
    /// the caller records them as failed rows and discovery stops re-enqueueing the files while
    /// they are unchanged.
    /// </summary>
    /// <param name="writeBatch">Extracted items; several may share a content hash.</param>
    /// <param name="writeChunk">
    /// Atomic writer for one chunk of items: it writes all of them or throws, leaving nothing
    /// behind (DatabaseWriterHelper.InsertOrUpdateBatch, one transaction per call).
    /// </param>
    public static IReadOnlyList<FileIndexBatchItem> Write(
        IReadOnlyList<FileIndexBatchItem> writeBatch,
        Func<IReadOnlyList<FileIndexBatchItem>, IReadOnlyDictionary<string, long>> writeChunk)
    {
        if (writeBatch.Count == 0) return Array.Empty<FileIndexBatchItem>();

        // Files processed in the same batch never see each other in the database, so duplicates
        // of the same content are resolved here: the first item for a content hash stays the
        // source row, the rest become duplicates referencing it once its row id is known.
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

        var failures = new List<(FileIndexBatchItem Item, Exception Error)>();
        var idByPath = WriteIsolatingFailures(sources, writeChunk, failures);

        var resolvedDuplicates = duplicates
            .Select(d => idByPath.TryGetValue(d.SourcePath, out var sourceId)
                ? d.Item with { Content = string.Empty, ContentRef = sourceId }
                : d.Item) // degrade to a normal indexed row if the source is missing
            .ToList();

        if (resolvedDuplicates.Count > 0)
            WriteIsolatingFailures(resolvedDuplicates, writeChunk, failures);

        ReportFailures(writeBatch.Count, failures);

        return failures
            .Select(f => f.Item with { Content = string.Empty, ContentHash = null, ContentRef = null })
            .ToList();
    }

    /// <summary>
    /// Reports the items the database refused. One file failing inside an otherwise healthy batch
    /// is a per-file diagnostic; a batch in which nothing at all could be written (a database out
    /// of disk space, say) is one condition, so it gets one line naming the first offender rather
    /// than an identical line per file.
    ///
    /// Note what is deliberately absent: nothing here decides on its own that a file is failed.
    /// The caller writes the returned items as failed rows, so a database that refuses those too
    /// (still no space, now read-only) throws from that write and the batch-level failure it has
    /// always been reaches the scheduler -- nothing is mislabelled as failed by it.
    /// </summary>
    private static void ReportFailures(int batchSize, List<(FileIndexBatchItem Item, Exception Error)> failures)
    {
        if (failures.Count == 0) return;

        if (failures.Count == batchSize)
        {
            PluginSdk.Logger.Log(
                $"[ContentSearch] None of the {batchSize} file(s) in this batch could be written to the index (first: '{failures[0].Item.Path}': {failures[0].Error.Message}); recording them as failed extractions",
                PluginSdk.LogLevel.Warn);
            return;
        }

        foreach (var (item, error) in failures)
        {
            PluginSdk.Logger.Log(
                $"[ContentSearch] Could not write '{item.Path}' to the index, recording it as a failed extraction: {error.Message}",
                PluginSdk.LogLevel.Warn);
        }
    }

    /// <summary>
    /// Writes one chunk, halving it every time the atomic write throws, until the item the
    /// database refuses stands alone: that one is collected as a failure while its neighbours are
    /// written normally. A failed transaction leaves nothing behind, which is what makes halving
    /// safe -- the failure can only be caused by an item inside the chunk that failed.
    /// </summary>
    private static Dictionary<string, long> WriteIsolatingFailures(
        IReadOnlyList<FileIndexBatchItem> items,
        Func<IReadOnlyList<FileIndexBatchItem>, IReadOnlyDictionary<string, long>> writeChunk,
        List<(FileIndexBatchItem Item, Exception Error)> failures)
    {
        var idByPath = new Dictionary<string, long>(items.Count, StringComparer.OrdinalIgnoreCase);

        // ponytail: a batch in which nothing can be written costs one transaction per item while
        // it is halved down, instead of the single transaction the atomic write used to spend.
        // That only happens when the database itself is refusing writes, where the caller's
        // failed-row write throws and the batch is dropped and retried as before.
        var pending = new Stack<IReadOnlyList<FileIndexBatchItem>>();
        pending.Push(items);

        while (pending.Count > 0)
        {
            var chunk = pending.Pop();
            try
            {
                foreach (var pair in writeChunk(chunk))
                    idByPath[pair.Key] = pair.Value;
            }
            catch (OperationCanceledException)
            {
                // Cancellation voids a batch rather than failing its items: the caller must keep
                // seeing it, exactly as it did when the write was a single call.
                throw;
            }
            catch (Exception) when (chunk.Count > 1)
            {
                var mid = chunk.Count / 2;
                pending.Push(chunk.Skip(mid).ToList());
                pending.Push(chunk.Take(mid).ToList());
            }
            catch (Exception ex)
            {
                failures.Add((chunk[0], ex));
            }
        }

        return idByPath;
    }
}
