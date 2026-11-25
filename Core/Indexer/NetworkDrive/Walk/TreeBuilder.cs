using System.ComponentModel;
using System.Diagnostics;
using System.Threading.Channels;

using Lertaro.Core.Indexer.NetworkDrive.Scheduling;
namespace Lertaro.Core.Indexer.NetworkDrive.Walk;

// TreeBuilderCheckpointExtensions publishes snapshots; TreeBuilderDiffExtensions reuses unchanged
// records. Their access to shared walk state requires the relevant members to be internal.
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
    // Linked to the caller's token so the walk can also end itself once its root is gone (see
    // AbandonIfRootLost). ponytail: never disposed -- a straggling worker may still touch _token's
    // WaitHandle after Run returns; the registration on the caller's token goes when that source does.
    private readonly CancellationTokenSource _abort;
    // Progress and checkpoint callbacks run under the read side; AbandonIfRootLost cancels under the write
    // side. So the abandon waits for a callback already running, and none starts after it: everything the
    // walk publishes lands before Run throws and the caller reverts the status. Readers never block each
    // other, so a slow checkpoint write does not stall other workers' progress. Not disposed, like _abort.
    internal readonly ReaderWriterLockSlim _callbackGate = new();
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

    // Diff-aware reuse state (TreeBuilderDiffExtensions): re-committing a directory's cached records
    // instead of rebuilding them from the listing once TreeDiffBaseline confirms nothing changed (the
    // listing itself is still taken), and tracking
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
        _abort = CancellationTokenSource.CreateLinkedTokenSource(token);
        _token = _abort.Token;
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
                try
                {
                    _token.ThrowIfCancellationRequested();
                    WalkDirectory(current);
                }
                finally
                {
                    // Retired even when the item throws: this is the walker's ONLY decrement and its only
                    // TryComplete, so an item that faulted its worker without reaching them left every
                    // surviving worker blocked in WaitToReadAsync forever -- a drive stuck on "indexing"
                    // with nothing watching for it. The faulted task itself is still observed by the
                    // ContinueWith in Run().
                    if (Interlocked.Decrement(ref _pendingDirectories) == 0)
                        _pending.Writer.TryComplete();
                }
            }
        }
    }

    internal void WalkDirectory(
        WorkItem current,
        Func<string, IEnumerable<NativeFileEntry>>? enumerate = null,
        Func<int, bool>? waitForCancellation = null)
    {
        var stopwatch = Stopwatch.StartNew();
        if (!this.TryEnumerateChildren(current.Path, out var children, out var failure, enumerate, waitForCancellation))
        {
            this.CountEnumerationFailure(current.Path, failure!);
            AbandonIfRootLost(failure!);
            this.CarryForwardPreviousSubtree(current, current.IgnoreRules, failure!, alreadyCommitted: null);
            return;
        }

        // Buffered whenever the previous walk knows something under this directory, Listed or not: a listing
        // that fails midway must say which names it captured so the rest can be carried forward, and an
        // un-Listed directory is typically what a previous failure leaves behind. A directory the
        // baseline has nothing for streams, since there is nothing to carry. ponytail: one buffered listing
        // per worker is held in memory at a time, so a single directory with millions of entries costs
        // that listing's size; the upgrade is a streaming name set instead of the entry list.
        Exception? enumerationFailure = null;
        List<NativeFileEntry>? liveEntries = null;
        if (_diffBaseline != null
            && (_diffBaseline.HasChildren(current.LocalId) || _diffBaseline.TryGetListedChildren(current.LocalId, out _)))
        {
            liveEntries = new List<NativeFileEntry>();
            try
            {
                foreach (var child in children)
                {
                    _token.ThrowIfCancellationRequested();
                    liveEntries.Add(child);
                }
            }
            catch (Exception ex) when (TreeBuilderEnumerationExtensions.IsEnumerationFailure(ex))
            {
                enumerationFailure = ex;
            }

            children = liveEntries;
            if (enumerationFailure == null && this.TryReuseUnchangedDirectory(current, liveEntries))
                return;
        }

        // A listing cut short can't vouch that no ignore file exists, so only a complete one replaces the probes.
        var ignoreRules = _filter.LoadIgnoreRules(current.Path, current.LogicalPath, current.IgnoreRules,
            enumerationFailure == null ? liveEntries : null);
        // Failed midway, buffered or streamed: the root may have gone with it. Only a buffered listing knows
        // which names it captured, so only it carries the rest forward from the previous walk; a streamed one
        // has nothing in the baseline to carry.
        enumerationFailure = this.ConsumeChildren(current, ignoreRules, children, enumerationFailure);
        if (enumerationFailure != null)
        {
            AbandonIfRootLost(enumerationFailure);
            if (liveEntries != null)
                this.CarryForwardPreviousSubtree(current, ignoreRules, enumerationFailure,
                    liveEntries.Select(e => e.Name).ToHashSet(StringComparer.OrdinalIgnoreCase));
        }

        if (stopwatch.ElapsedMilliseconds >= 2_000)
            Interlocked.Increment(ref _slowDirectories);
    }

    // A failure while the root itself is gone means the share (or the removable drive) dropped mid-walk:
    // every directory still queued would fail the same way. End the walk instead -- the caller's
    // cancellation path keeps the index it already has. Whatever the error code: a deleted share answers
    // 67 for everything under it, and a walk that took that as "gone" saved 12,544 of 6,021,472 records
    // over the good index. The root's own listing counts too: the share can drop between the caller's
    // reachability check and the first enumeration. Only a failed directory pays for the probe.
    private void AbandonIfRootLost(Exception failure)
    {
        // ERROR_BAD_NET_NAME proves the share is gone even when SMB's attribute cache still answers
        // Directory.Exists with true. Do not replace a good index with this incomplete pass.
        var networkNameLost = failure is Win32Exception { NativeErrorCode: 67 }
            or IOException { HResult: unchecked((int)0x80070043) };
        if (!networkNameLost && Directory.Exists(_physicalRoot))
            return;

        Logger.Log($"[NetworkIndexer] {_physicalRoot} became unreachable mid-walk; abandoning this pass and keeping the existing index.", LogLevel.Warn);
        _callbackGate.EnterWriteLock();
        try
        {
            _abort.Cancel();
        }
        finally
        {
            _callbackGate.ExitWriteLock();
        }

        _token.ThrowIfCancellationRequested();
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
    internal void EnqueueDirectory(string path, string logicalPath, UInt128 parentId, int depth, NetworkIgnoreRuleSet ignoreRules,
        AncestorNode? parentAncestors = null, bool isReparsePoint = false)
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

        var nextAncestors = new AncestorNode(normalizedPath, parentAncestors, isReparsePoint);
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
