using System.Diagnostics;
using System.Threading.Channels;

using Lertaro.Core.Indexer.NetworkDrive.Scheduling;
namespace Lertaro.Core.Indexer.NetworkDrive.Walk;

// Checkpoint/snapshot logic lives in TreeBuilderCheckpointExtensions.cs and diff-reuse logic in
// TreeBuilderDiffExtensions.cs (extension methods, matching RuntimeIndex's BucketExtensions/
// QueryExtensions split) instead of partial classes, to keep this file under the project's line limit.
// Both need broad access to this walker's shared mutable state, so the fields/methods they touch are
// `internal` rather than `private`.
internal sealed class TreeBuilder
{
    internal const int RecordBatchSize = 256;
    internal const int ProgressBatchSize = 1024;
    internal const int CheckpointBatchSize = 5120;
    // See DoublingCheckpointGate's own header comment for why this doubling-with-cap scheme exists.
    internal const int MaxCheckpointBatchSize = 524288;
    internal readonly DoublingCheckpointGate _checkpointGate = new(CheckpointBatchSize, MaxCheckpointBatchSize);
    internal readonly FileRecordStore _store;
    private readonly string _root;
    private readonly string _physicalRoot;
    internal readonly WalkFilter _filter;
    internal readonly CancellationToken _token;
    internal readonly Action<int, int> _onProgress;
    internal readonly Action<FileRecordStore, NetworkDriveWalkStats>? _onCheckpoint;
    private readonly Channel<WorkItem> _pending;
    internal readonly object _recordsGate = new();
    internal readonly FileRecordNamePool _namePool = new();
    private readonly HashSet<UInt128> _enqueuedIds = new();
    private int _pendingDirectories;
    internal int _countSinceProgress;
    internal int _indexedItems;
    // Live files/dirs split of _indexedItems, for progress display -- _indexedItems itself stays the
    // single source of truth for the checkpoint threshold and the diagnostic log in Run().
    internal int _indexedFiles;
    internal int _indexedDirs;
    internal int _skippedItems;
    internal int _errors;
    internal int _enumerateErrors;
    internal int _attributeErrors;
    internal int _reparseSkipped;
    internal int _slowDirectories;
    internal int _reusedDirectories;

    // Diff-aware reuse state (TreeBuilderDiffExtensions): reusing a directory's cached children instead
    // of re-listing it over the network when TreeDiffBaseline confirms nothing changed, and tracking
    // which directories in THIS store have been fully enumerated (FileRecordFlags.Listed) so a future
    // resume can trust them the same way.
    internal readonly TreeDiffBaseline? _diffBaseline;
    // True when the exclusion rules fingerprint on the previous store doesn't match the current one --
    // see NetworkIndex.Build. A reused (mtime-unchanged) directory's cached children were filtered under
    // whatever rules were active *then*; a path just un-excluded since would never surface without this.
    internal readonly bool _recheckExclusions;
    internal readonly Dictionary<UInt128, int> _indexById = new();

    public TreeBuilder(
        FileRecordStore store,
        string root,
        string physicalRoot,
        WalkOptions options,
        CancellationToken token,
        Action<int, int> onProgress,
        Action<FileRecordStore, NetworkDriveWalkStats>? onCheckpoint = null,
        TreeDiffBaseline? diffBaseline = null,
        bool recheckExclusions = false)
    {
        _store = store;
        _root = PathHelpers.NormalizePath(root, true);
        _physicalRoot = PathHelpers.NormalizePath(physicalRoot, true);
        _filter = WalkFilter.Create(_root, options);
        _token = token;
        _onProgress = onProgress;
        _onCheckpoint = onCheckpoint;
        _diffBaseline = diffBaseline;
        _recheckExclusions = recheckExclusions;
        _pending = Channel.CreateUnbounded<WorkItem>(new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = false
        });
        this.RegisterDirectoryIndices(0, _store.Records);
    }

    public NetworkDriveWalkStats Run()
    {
        EnqueueDirectory(_physicalRoot, _root, parentId: 1, depth: 0, NetworkIgnoreRuleSet.Empty);
        var workers = GetWorkerCount();
        var tasks = new Task[workers];
        for (var i = 0; i < workers; i++)
        {
            var worker = DedicatedWorkerThread.Run(WorkerLoopAsync, "NetworkDriveScan");
            // Task.WaitAll(tasks, _token) below can return early via ITS OWN token cancelling while a
            // DIFFERENT worker is still running and later faults independently (a real fault, not a
            // cancellation -- DedicatedWorkerThread already handles the pure-cancellation case). That
            // worker's Task would then never get awaited/observed by anyone, and an unobserved faulted
            // Task crashes the whole process via TaskScheduler.UnobservedTaskException when the GC
            // finalizes it -- exactly what happened in the wild for a network-share timestamp bug this
            // continuation would have contained to a logged error instead. Touching .Exception marks a
            // faulted task observed regardless of whether WaitAll ever waited on it.
            worker.ContinueWith(t => Logger.Log($"[NetworkIndexer] Worker task faulted after WaitAll returned: {t.Exception}", LogLevel.Error),
                CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            tasks[i] = worker;
        }

        Task.WaitAll(tasks, _token);
        // Temporary diagnostic for a resumed scan finishing with far fewer records than the drive
        // actually has: shows whether the channel drained because there was genuinely nothing left
        // enqueued (_pendingDirectories == 0, as expected) or something else is going on, and how much of
        // the walk came from the reuse path vs real enumeration.
        Logger.Log($"[NetworkIndexer] TreeBuilder.Run done for {_physicalRoot}: pendingDirectories={Volatile.Read(ref _pendingDirectories)}, " +
            $"enqueuedIds={_enqueuedIds.Count}, records={_store.Records.Count}, indexedItems={Volatile.Read(ref _indexedItems)}, " +
            $"skipped={Volatile.Read(ref _skippedItems)}, errors={Volatile.Read(ref _errors)} " +
            $"(enumerate={Volatile.Read(ref _enumerateErrors)}, attribute={Volatile.Read(ref _attributeErrors)}), reused={Volatile.Read(ref _reusedDirectories)}.");
        return new NetworkDriveWalkStats(
            Volatile.Read(ref _skippedItems),
            Volatile.Read(ref _errors),
            Volatile.Read(ref _enumerateErrors),
            Volatile.Read(ref _attributeErrors),
            Volatile.Read(ref _reparseSkipped),
            Volatile.Read(ref _slowDirectories));
    }

    private async Task WorkerLoopAsync()
    {
        var reader = _pending.Reader;
        while (await reader.WaitToReadAsync(_token).ConfigureAwait(false))
        {
            while (reader.TryRead(out var current))
            {
                _token.ThrowIfCancellationRequested();
                WalkDirectory(current);
                if (Interlocked.Decrement(ref _pendingDirectories) == 0)
                    _pending.Writer.TryComplete();
            }
        }
    }

    private void WalkDirectory(WorkItem current)
    {
        if (_diffBaseline != null && this.TryReuseUnchangedDirectory(current))
            return;

        var ignoreRules = _filter.LoadIgnoreRules(current.Path, current.LogicalPath, current.IgnoreRules);
        var stopwatch = Stopwatch.StartNew();
        IEnumerable<NativeFileEntry> children = null!;
        var success = false;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try { children = NativeFileEnumerator.Enumerate(current.Path); success = true; break; }
            catch when (attempt < 2 && !_token.IsCancellationRequested) { Thread.Sleep(100 * (attempt + 1)); }
            catch { break; }
        }
        if (!success) { this.CountError(ref _enumerateErrors); return; }

        var batch = new List<FileRecord>(RecordBatchSize);
        foreach (var child in children)
        {
            _token.ThrowIfCancellationRequested();

            var childPath = current.Path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar + child.Name;
            var createResult = this.TryCreateRecord(childPath, current.LogicalPath, current.LocalId, out var record, out var isDirectory, out var logicalFullPath);
            if (createResult != WalkRecordResult.Success)
            {
                this.CountCreateFailure(createResult);
                continue;
            }

            if (!_filter.ShouldIndex(logicalFullPath, record.Name, isDirectory, record.Attributes, ignoreRules))
            {
                Interlocked.Increment(ref _skippedItems);
                continue;
            }

            batch.Add(record);
            if (batch.Count >= RecordBatchSize)
                FlushRecords(batch);

            var indexedItems = Interlocked.Increment(ref _indexedItems);
            if (isDirectory) Interlocked.Increment(ref _indexedDirs); else Interlocked.Increment(ref _indexedFiles);

            if (isDirectory && _filter.ShouldDescend(logicalFullPath, record.Attributes, current.Depth + 1, ignoreRules))
            {
                // A directory just added to batch isn't in _indexById until its batch is flushed -- another
                // worker can dequeue and finish this child (including its own MarkListed) before that
                // happens, silently leaving it un-Listed forever. Flush now so the child's own record is
                // registered before anyone else can possibly touch it.
                FlushRecords(batch);
                EnqueueDirectory(childPath, logicalFullPath, record.Id, current.Depth + 1, ignoreRules, current.Ancestors);
            }

            if (Interlocked.Increment(ref _countSinceProgress) >= ProgressBatchSize)
            {
                Interlocked.Exchange(ref _countSinceProgress, 0);
                _onProgress(Volatile.Read(ref _indexedFiles), Volatile.Read(ref _indexedDirs));
            }

            this.MaybeCheckpoint(indexedItems);
        }

        FlushRecords(batch);
        this.MarkListed(current.LocalId);
        if (stopwatch.ElapsedMilliseconds >= 2_000)
            Interlocked.Increment(ref _slowDirectories);
    }

    internal void FlushRecords(List<FileRecord> batch)
    {
        if (batch.Count == 0)
            return;

        lock (_recordsGate)
        {
            var startIndex = _store.Records.Count;
            _store.Records.AddRange(batch);
            this.RegisterDirectoryIndices(startIndex, batch);
        }

        batch.Clear();
    }

    private int GetWorkerCount() => _filter.WorkerCount > 0
            ? Math.Clamp(_filter.WorkerCount, 1, 32)
            : Math.Clamp(Environment.ProcessorCount, 2, 4);

    // parentId here is this directory's OWN id (becomes WorkItem.LocalId), not its parent's -- matches the
    // naming TryCreateRecord's callers already use when they pass record.Id through as this parameter.
    internal void EnqueueDirectory(string path, string logicalPath, UInt128 parentId, int depth, NetworkIgnoreRuleSet ignoreRules, AncestorNode? parentAncestors = null)
    {
        if (depth > 128)
        {
            Interlocked.Increment(ref _reparseSkipped);
            Interlocked.Increment(ref _skippedItems);
            return;
        }

        var normalizedPath = PathHelpers.NormalizePath(path, true).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (parentAncestors != null)
        {
            if (parentAncestors.Contains(normalizedPath))
            {
                Interlocked.Increment(ref _reparseSkipped);
                Interlocked.Increment(ref _skippedItems);
                return;
            }

            try
            {
                var resolvedTarget = Directory.ResolveLinkTarget(path, returnFinalTarget: true);
                if (resolvedTarget != null && parentAncestors.Contains(resolvedTarget.FullName))
                {
                    Interlocked.Increment(ref _reparseSkipped);
                    Interlocked.Increment(ref _skippedItems);
                    return;
                }
            }
            catch
            {
            }
        }

        var nextAncestors = new AncestorNode(normalizedPath, parentAncestors);
        if (nextAncestors.HasSegmentCycle())
        {
            Interlocked.Increment(ref _reparseSkipped);
            Interlocked.Increment(ref _skippedItems);
            return;
        }

        // Last-resort guard against processing the same directory twice in one run: a corrupted diff
        // baseline (e.g. a duplicate row left by some earlier bug) could otherwise get a directory enqueued
        // more than once, and each duplicate walks or copies its entire subtree again -- compounding into
        // unbounded growth rather than just carrying the original duplication forward unchanged. A given
        // directory id can only legitimately be discovered once per run, so refusing every id after its
        // first enqueue is always safe, never drops a real directory.
        lock (_recordsGate)
        {
            if (!_enqueuedIds.Add(parentId))
                return;
        }

        Interlocked.Increment(ref _pendingDirectories);
        try
        {
            _pending.Writer.WriteAsync(new WorkItem(path, logicalPath, parentId, depth, ignoreRules, nextAncestors), _token)
                .AsTask()
                .GetAwaiter()
                .GetResult();
        }
        catch
        {
            Interlocked.Decrement(ref _pendingDirectories);
            throw;
        }
    }

}
