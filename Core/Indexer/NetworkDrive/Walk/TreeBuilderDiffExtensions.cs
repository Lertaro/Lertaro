namespace Lertaro.Core.Indexer.NetworkDrive.Walk;

// Diff-aware half of TreeBuilder, as extension methods (matching RuntimeIndex's BucketExtensions/
// QueryExtensions split) instead of a partial class, to keep TreeBuilder.cs under the project's line
// limit: reusing a directory's cached children instead of re-listing it over the network when
// TreeDiffBaseline confirms nothing changed, and tracking which directories in THIS store have been
// fully enumerated (FileRecordFlags.Listed) so a future resume can trust them the same way.
internal static class TreeBuilderDiffExtensions
{
    // Must be called with builder._recordsGate already held (both call sites -- the constructor seeding
    // any pre-existing records, and FlushRecords -- already hold it or run before workers start).
    public static void RegisterDirectoryIndices(this TreeBuilder builder, int startIndex, List<FileRecord> records)
    {
        for (var i = 0; i < records.Count; i++)
        {
            if (records[i].IsDirectory)
                builder._indexById[records[i].Id] = startIndex + i;
        }
    }

    public static void MarkListed(this TreeBuilder builder, UInt128 id)
    {
        lock (builder._recordsGate)
        {
            if (!builder._indexById.TryGetValue(id, out var index))
                return;

            var r = builder._store.Records[index];
            if ((r.Flags & FileRecordFlags.Listed) != 0)
                return;

            builder._store.Records[index] = new FileRecord(
                r.Id, r.ParentId, r.Name, r.Flags | FileRecordFlags.Listed,
                r.Size, r.CreationTimeUnixSeconds, r.LastWriteTimeUnixSeconds, r.LastAccessTimeUnixSeconds);
        }
    }

    // Reuses current's previously-recorded children wholesale instead of listing the directory again, when
    // TreeDiffBaseline confirms it was fully captured last time and hasn't changed since. Still recurses
    // into every cached child directory individually -- a directory's own LastWriteTime only reflects its
    // direct children, never anything deeper -- so this only ever skips ONE level of listing per call, not
    // an entire subtree at once.
    public static bool TryReuseUnchangedDirectory(this TreeBuilder builder, WorkItem current)
    {
        if (!builder._diffBaseline!.TryGetUnchangedChildren(current.Path, current.LocalId, out var previousChildren))
            return false;

        Interlocked.Increment(ref builder._reusedDirectories);

        var ignoreRules = builder._filter.LoadIgnoreRules(current.Path, current.LogicalPath, current.IgnoreRules);
        var batch = new List<FileRecord>(TreeBuilder.RecordBatchSize);
        // Only populated when a recheck is actually needed -- ReconcileLiveEntries below uses it to tell
        // "already accounted for from cache" apart from "new to us", without a second full pass over
        // previousChildren.
        var previousByName = builder._recheckExclusions ? new Dictionary<string, FileRecord>(StringComparer.OrdinalIgnoreCase) : null;

        foreach (var child in previousChildren)
        {
            builder._token.ThrowIfCancellationRequested();
            previousByName?.TryAdd(child.Name, child);

            var isDirectory = child.IsDirectory;
            var attributes = FileRecordFlagsHelper.ToAttributes(child.Flags);
            var logicalFullPath = PathHelpers.NormalizePath(Path.Combine(current.LogicalPath, child.Name), isDirectory);

            if (!builder._filter.ShouldIndex(logicalFullPath, child.Name, isDirectory, attributes, ignoreRules))
            {
                Interlocked.Increment(ref builder._skippedItems);
                continue;
            }

            batch.Add(child);
            if (batch.Count >= TreeBuilder.RecordBatchSize)
                builder.FlushRecords(batch);

            var indexedItems = Interlocked.Increment(ref builder._indexedItems);
            if (isDirectory) Interlocked.Increment(ref builder._indexedDirs); else Interlocked.Increment(ref builder._indexedFiles);

            if (isDirectory && builder._filter.ShouldDescend(logicalFullPath, attributes, current.Depth + 1, ignoreRules))
            {
                // Same ordering requirement as WalkDirectory's fresh-listing path: flush before enqueueing
                // so this child's own record is in _indexById before another worker can dequeue it.
                builder.FlushRecords(batch);
                var physicalChildPath = Path.Combine(current.Path, child.Name);
                builder.EnqueueDirectory(physicalChildPath, logicalFullPath, child.Id, current.Depth + 1, ignoreRules, current.Ancestors);
            }

            if (Interlocked.Increment(ref builder._countSinceProgress) >= TreeBuilder.ProgressBatchSize)
            {
                Interlocked.Exchange(ref builder._countSinceProgress, 0);
                builder._onProgress(Volatile.Read(ref builder._indexedFiles), Volatile.Read(ref builder._indexedDirs));
            }

            builder.MaybeCheckpoint(indexedItems);
        }

        if (previousByName != null)
            ReconcileLiveEntries(builder, current, ignoreRules, previousByName, batch);

        builder.FlushRecords(batch);
        builder.MarkListed(current.LocalId);
        return true;
    }

    // Only reached when exclusion rules may have changed since this directory was last fully listed (see
    // _recheckExclusions). Lists the directory once -- one round trip, cheap next to a per-item stat -- and
    // processes only the names the cache-driven pass above didn't already account for: something the old
    // rules excluded and the new ones don't (or, defensively, a name that's genuinely new despite the
    // parent's unchanged mtime -- shouldn't happen if mtime is reliable, but costs nothing extra to allow
    // for). Anything cached that no longer shows up here just silently isn't re-added to batch -- the
    // deletion side of the same coin, self-correcting even if mtime turns out unreliable on some filesystem.
    private static void ReconcileLiveEntries(TreeBuilder builder, WorkItem current, NetworkIgnoreRuleSet ignoreRules, Dictionary<string, FileRecord> previousByName, List<FileRecord> batch)
    {
        IEnumerable<string> liveEntries;
        try
        {
            liveEntries = Directory.EnumerateFileSystemEntries(current.Path);
        }
        catch
        {
            builder.CountError(ref builder._enumerateErrors);
            return;
        }

        foreach (var entry in liveEntries)
        {
            builder._token.ThrowIfCancellationRequested();

            var name = Path.GetFileName(entry.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrEmpty(name) || previousByName.ContainsKey(name))
                continue;

            var createResult = builder.TryCreateRecord(entry, current.LogicalPath, current.LocalId, out var record, out var isDirectory, out var logicalFullPath);
            if (createResult != WalkRecordResult.Success)
            {
                builder.CountCreateFailure(createResult);
                continue;
            }

            if (!builder._filter.ShouldIndex(logicalFullPath, record.Name, isDirectory, record.Attributes, ignoreRules))
            {
                Interlocked.Increment(ref builder._skippedItems);
                continue;
            }

            batch.Add(record);
            if (batch.Count >= TreeBuilder.RecordBatchSize)
                builder.FlushRecords(batch);

            var indexedItems = Interlocked.Increment(ref builder._indexedItems);
            if (isDirectory) Interlocked.Increment(ref builder._indexedDirs); else Interlocked.Increment(ref builder._indexedFiles);

            if (isDirectory && builder._filter.ShouldDescend(logicalFullPath, record.Attributes, current.Depth + 1, ignoreRules))
            {
                builder.FlushRecords(batch);
                builder.EnqueueDirectory(entry, logicalFullPath, record.Id, current.Depth + 1, ignoreRules, current.Ancestors);
            }

            if (Interlocked.Increment(ref builder._countSinceProgress) >= TreeBuilder.ProgressBatchSize)
            {
                Interlocked.Exchange(ref builder._countSinceProgress, 0);
                builder._onProgress(Volatile.Read(ref builder._indexedFiles), Volatile.Read(ref builder._indexedDirs));
            }

            builder.MaybeCheckpoint(indexedItems);
        }
    }
}
