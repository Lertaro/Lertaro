using Lertaro.Core.IndexV2;
using Lertaro.Core.IndexV2.Alias;
using Lertaro.Core.IndexV2.Delta;
using Lertaro.Core.IndexV2.Search;
using Lertaro.Core.IndexV2.Persistence;
using Lertaro.Core.IndexV2.Space;
using Lertaro.Core.Indexer.NetworkDrive.Walk;
using Lertaro.Core.SearchIndex.Query;
namespace Lertaro.Core.Indexer.NetworkDrive;

// Wraps one network/WSL/folder LiveIndex. Its mutable scan metadata is stamped during Save/ToStore.
// Replaced instances must be disposed; readers treat a concurrent disposal as a skipped stale source.
internal sealed class NetworkIndex : IDisposable
{
    private LiveIndex? _live;

    public NetworkIndex(string drive) => Drive = drive;

    public string Drive { get; }
    public DateTime LastUpdated { get; set; } = DateTime.Now;
    public bool IsComplete { get; set; }
    public string ExclusionRulesFingerprint { get; set; } = string.Empty;
    public UInt128 RootId { get; private set; }
    public int Skipped { get; private set; }
    public int Errors { get; private set; }
    public int EnumerateErrors { get; private set; }
    public int AttributeErrors { get; private set; }
    public int ReparseSkipped { get; private set; }
    public int SlowDirectories { get; private set; }
    public int Count
    {
        get
        {
            if (_live == null)
                return 0;
            try
            {
                var (files, dirs) = _live.GetCounts();
                return Math.Max(0, files + dirs - 1); // -1: exclude the root row itself
            }
            catch (ObjectDisposedException)
            {
                // Swapped out mid-poll, same as SearchStreaming below -- report empty rather than crash.
                return 0;
            }
        }
    }

    // Shared tail of every construction path that starts from a fresh store.
    public static NetworkIndex FromStore(FileRecordStore store)
    {
        var index = new NetworkIndex(store.SourceKey)
        {
            RootId = store.RootId,
            LastUpdated = store.LastUpdated,
            IsComplete = store.IsComplete,
            ExclusionRulesFingerprint = store.ExclusionRulesFingerprint,
        };
        var path = NetworkDriveCacheLocator.GetCachePath(store.SourceKey);
        SnapshotWriter.Write(store, path);
        index._live = new LiveIndex(Snapshot.Open(path));
        return index;
    }

    public static NetworkIndex FromStore(FileRecordStore store, NetworkDriveWalkStats stats)
    {
        var index = FromStore(store);
        index.Skipped = stats.Skipped;
        index.Errors = stats.Errors;
        index.EnumerateErrors = stats.EnumerateErrors;
        index.AttributeErrors = stats.AttributeErrors;
        index.ReparseSkipped = stats.ReparseSkipped;
        index.SlowDirectories = stats.SlowDirectories;
        index.LastUpdated = DateTime.Now;
        return index;
    }

    // Fast startup path for an existing V2 cache.
    internal static NetworkIndex FromSnapshotFile(string drive, string path)
    {
        var snapshot = Snapshot.Open(path);
        var live = new LiveIndex(snapshot);
        var index = new NetworkIndex(drive)
        {
            RootId = snapshot.RootId,
            LastUpdated = snapshot.LastUpdated,
            IsComplete = snapshot.IsComplete,
            ExclusionRulesFingerprint = snapshot.ExclusionRulesFingerprint,
            _live = live,
        };
        AliasProvidersReconciler.ReconcileIfStale(live, path, drive);
        return index;
    }

    public static NetworkIndex Build(
        string drive,
        string root,
        string physicalRoot,
        WalkOptions options,
        CancellationToken token,
        Action<int, int> onProgress,
        Action<FileRecordStore, NetworkDriveWalkStats>? onCheckpoint = null,
        FileRecordStore? previousStore = null,
        Action? beforeFinalWrite = null)
    {
        const ulong rootId = 1;
        // Setting this on the store itself (not just on `index` after the walk finishes) means every
        // mid-walk checkpoint -- which serializes this same store, see TreeBuilder.CloneStore -- already
        // carries the right fingerprint too, not just the final save.
        var fingerprint = IndexerHelper.ComputeExclusionFingerprint(options.ExcludedPaths, options.IgnoredPathGlobs, options.IgnoredPathRegexes);
        var store = new FileRecordStore
        {
            SourceKey = drive,
            SourceKind = FileRecordSourceKind.NetworkMappedDrive,
            IdKind = FileRecordIdKind.SourceLocalId64,
            RootId = rootId,
            ExclusionRulesFingerprint = fingerprint
        };
        var rootLastWriteTime = FileTimeHelper.TryGetLastWriteTimeUnixSeconds(physicalRoot);

        store.Records.Add(new FileRecord(
            rootId,
            rootId,
            string.Empty,
            FileRecordFlags.Directory | FileRecordFlags.SourceRoot,
            lastWriteTimeUnixSeconds: rootLastWriteTime));

        var diffBaseline = TreeDiffBaseline.From(previousStore);
        // No previous store at all means this is a first-ever scan -- nothing to recheck, the normal fresh
        // walk already covers everything. Otherwise, a fingerprint mismatch is the only thing that can make
        // a reused (mtime-unchanged) directory's recorded children incomplete under the *current* rules --
        // see TryReuseUnchangedDirectory's add/remove diff.
        var recheckExclusions = previousStore != null && previousStore.ExclusionRulesFingerprint != fingerprint;
        var builder = new TreeBuilder(store, root, physicalRoot, options, token, onProgress, onCheckpoint, diffBaseline, recheckExclusions);
        var stats = builder.Run();

        // FromStore below writes straight over this drive's cache path -- give the caller a chance to
        // release whatever it still has that path memory-mapped (the cached NetworkIndex this whole
        // refresh has been serving searches from throughout the walk) right before that happens, same
        // fix and for the same reason as NetworkIndexerPublisher.PublishCheckpoint's own periodic writes.
        beforeFinalWrite?.Invoke();
        var index = FromStore(store, stats);
        index.RootId = rootId;
        index.ExclusionRulesFingerprint = fingerprint;
        // NetworkIndexStatus.Items has no files/dirs split to preserve -- report the true final total under
        // "files" so every existing status consumer sees the same number it always did.
        onProgress(index.Count, 0);
        return index;
    }

    public FileRecordStore ToStore()
    {
        if (_live == null)
            return new FileRecordStore { SourceKey = Drive, SourceKind = FileRecordSourceKind.NetworkMappedDrive, IdKind = FileRecordIdKind.SourceLocalId64, RootId = RootId };
        var store = _live.ToStore();
        // ToStore() reads these back off the snapshot's OWN frozen header (as of last compaction) --
        // overwrite with this NetworkIndex's current values, which may be newer (see the class comment).
        store.LastUpdated = LastUpdated;
        store.IsComplete = IsComplete;
        store.ExclusionRulesFingerprint = ExclusionRulesFingerprint;
        return store;
    }

    // Folds the current live state into `path` (the drive's own V2 cache file) and swaps it in,
    // stamping this NetworkIndex's own IsComplete/ExclusionRulesFingerprint/LastUpdated -- mirrors the
    // old engine's unconditional-write SaveDrivesToCache semantics (force:true; a caller decides
    // whether/when to call this at all, so there's no periodic-skip case to support here).
    internal void SaveToCache(string path)
    {
        if (_live == null)
            return;
        var stamp = new CompactionStamp(IsComplete: IsComplete, ExclusionRulesFingerprint: ExclusionRulesFingerprint, LastUpdated: LastUpdated);
        _live.Compact(path, stamp, force: true);
    }

    public void SearchStreaming(ParsedSearchQuery parsed, string rawQuery, string? directoryFilterLower, int limit, Action<SearchResult> onResult, CancellationToken token, string? fileNameFilter = null)
    {
        if (_live == null)
            return;
        try
        {
            IndexV2Searcher.SearchStreaming(_live, rawQuery, limit, onResult, token, directoryFilterLower, fileNameFilter);
        }
        catch (ObjectDisposedException)
        {
            // This drive's index was swapped out (checkpoint/refresh/delete) mid-search -- treat as no
            // results from the stale snapshot rather than crashing the caller.
        }
    }

    // Directory listing rather than search: the same walk local drives get, over this drive's own
    // LiveIndex -- network/WSL/folder indexes live in this process, not in the elevated service, so a
    // caller enumerating a share has to come through here to reach one. False = this index doesn't
    // hold that path (wrong drive, or the directory isn't in it), so the caller can try elsewhere.
    public bool EnumerateDirectory(string path, bool recursive, string[]? patterns, int limit, Action<SearchResult> onResult, CancellationToken token)
    {
        if (_live == null)
            return false;
        try
        {
            return IndexV2Searcher.EnumerateDirectory(_live, path, recursive, patterns, limit, onResult, token);
        }
        catch (ObjectDisposedException)
        {
            // Swapped out mid-walk (checkpoint/refresh/delete), same as SearchStreaming above -- report
            // "not held here" so the caller falls back rather than reporting an empty directory.
            return false;
        }
    }

    internal SpaceQueryResult GetSpaceEntries(string? directory)
    {
        if (_live == null)
            return SpaceQueryResult.NotFound;
        try
        {
            return LiveSpaceQuery.GetEntries(_live, directory);
        }
        catch (ObjectDisposedException)
        {
            return SpaceQueryResult.NotFound;
        }
    }

    public void ClearPathCache()
    {
        // IndexV2's Snapshot has no per-row path memo to clear -- see UsnIndexer.ClearAllPathCaches.
    }

    public void ClearCaches()
    {
        // IndexV2 has no cross-search rank/candidate cache yet -- see SearchCoordinator.ClearCaches.
    }

    public void CollectRecentFiles(string dirLower, uint cutoffUtc, List<SearchResult> candidates)
    {
        if (_live == null)
            return;
        try
        {
            IndexV2Searcher.GetRecentFiles(_live, dirLower, cutoffUtc, candidates);
        }
        catch (ObjectDisposedException)
        {
        }
    }

    public bool ApplyCreatedOrChanged(string root, string path, ExclusionRuleSet? exclusionRules = null)
        => ApplyToLive(delta => DeltaPathApplier.ApplyCreatedOrChanged(delta, RootId, root, path, exclusionRules));

    public bool ApplyDeleted(string path)
        => ApplyToLive(delta => DeltaPathApplier.ApplyDeleted(delta, path));

    public bool ApplyRenamed(string root, string oldPath, string newPath, ExclusionRuleSet? exclusionRules = null)
        => ApplyToLive(delta => DeltaPathApplier.ApplyRenamed(delta, RootId, root, oldPath, newPath, exclusionRules));

    // Shared tail of the three Apply* watchers above: run one mutation against the live index, stamp
    // LastUpdated when it changed anything, and treat a mid-mutation swap-out (ObjectDisposedException,
    // same as SearchStreaming above) as "nothing applied".
    private bool ApplyToLive(Func<DeltaOverlay, bool> apply)
    {
        if (_live == null)
            return false;
        try
        {
            var changed = false;
            _live.Mutate((_, delta) => changed = apply(delta));
            if (changed)
                LastUpdated = DateTime.Now;
            return changed;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        _live?.Dispose();
        _live = null;
    }
}
