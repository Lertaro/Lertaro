using Lertaro.Core.IndexV2.Alias;

using Lertaro.Core.IndexV2.Persistence;
namespace Lertaro.Core.IndexV2.Delta;

// The live-update overlay: the mmap Snapshot stays immutable; changes land here as base-row
// tombstones (delete), base-row overrides (rename/move/metadata -- rows keep their identity so
// children's parent links stay valid), and appended rows for brand-new ids. Search runs base-then-
// delta; Compaction periodically folds this into a fresh snapshot. A single production invariant this
// whole overlay relies on: AT MOST ONE LIVE DIRECTORY ROW PER FRN -- guaranteed by real USN streams
// (directories can't be hard-linked; a rename pair tombstones the old record before adding the new),
// verified against the reference engine under 20-seed randomized fuzzing (see IndexMemBench).
public sealed class DeltaOverlay
{
    internal sealed class DeltaRecord
    {
        public UInt128 Id;
        public string Name = string.Empty;
        public ushort Flags;
        public long Size;
        public uint Creation, LastWrite, LastAccess;
        // Resolved base-row parent when known at insert time; -1 with ParentFrn kept for lazy
        // resolution (out-of-order arrivals: the parent may be a later delta row, or never come).
        public int ParentBaseRow = -1;
        public UInt128 ParentFrn;
        public string[]? Aliases;
        public byte[] ProviderIds = Array.Empty<byte>();
        public bool Removed;
    }

    internal readonly Snapshot Snapshot;
    internal readonly HashSet<int> DeletedBase = new();
    internal readonly Dictionary<int, DeltaRecord> BaseOverrides = new();
    internal readonly List<DeltaRecord> Added = new();
    // Base directory rows removed by a USN RENAME_OLD_NAME record: the FRN survives under a new name
    // (an AddLink follows), so children are NOT cascaded -- path walks forward through the FRN once
    // the new-name row arrives (mirrors HardLinkDelta.RemoveLinkForRename + ReorphanChildren).
    internal readonly Dictionary<int, UInt128> RenamedAway = new();
    // Size/timestamp refreshes (USN DATA_*/BASIC_INFO_CHANGE -> re-stat) for otherwise-untouched base
    // rows; rows with a full override record carry the refresh inside that record instead.
    internal readonly Dictionary<int, (long Size, uint Creation, uint LastWrite, uint LastAccess)> MetadataOverrides = new();
    private readonly Dictionary<UInt128, int> _addedById = new();

    public DeltaOverlay(Snapshot snapshot) => Snapshot = snapshot;

    public int VisibleAddedCount => Added.Count(r => !r.Removed);
    public int PendingChangeCount => DeletedBase.Count + BaseOverrides.Count + RenamedAway.Count + MetadataOverrides.Count + Added.Count;

    // Running adjustment to Snapshot.TotalFiles/TotalDirs from every visibility change this overlay
    // has made -- LiveIndex.GetCounts() adds these to the base snapshot's frozen totals instead of
    // rescanning millions of rows on every status poll. Mirrors RuntimeIndex.TotalFiles/TotalDirs,
    // which the old engine updates at the same transition points (Upsert/Remove/MarkRowDeleted/
    // AppendHardLink) rather than recomputing.
    public int FileCountDelta { get; private set; }
    public int DirCountDelta { get; private set; }

    internal void CountAdded(bool isDirectory)
    {
        if (isDirectory) DirCountDelta++; else FileCountDelta++;
    }

    internal void CountRemoved(bool isDirectory)
    {
        if (isDirectory) DirCountDelta--; else FileCountDelta--;
    }

    private static bool IsDir(ushort flags) => (flags & (ushort)FileRecordFlags.Directory) != 0;

    public void Upsert(UInt128 id, UInt128 parentId, string name, FileRecordFlags flags, long size, uint creation, uint lastWrite, uint lastAccess)
    {
        var isDirectory = (flags & FileRecordFlags.Directory) != 0;
        var record = new DeltaRecord
        {
            Id = id,
            Name = name,
            Flags = (ushort)flags,
            Size = size,
            Creation = creation,
            LastWrite = lastWrite,
            LastAccess = lastAccess,
            ParentBaseRow = ResolveParentRow(id, parentId),
            ParentFrn = parentId,
        };
        record.Aliases = AliasGeneration.Generate(name, out var providerIds);
        record.ProviderIds = providerIds;

        if (_addedById.TryGetValue(id, out var deltaIdx))
        {
            var existing = Added[deltaIdx];
            var wasDirectory = IsDir(existing.Flags);
            if (existing.Removed)
            {
                // Resurrection: the earlier Remove already decounted this row, so bringing it back
                // counts as a fresh add (the prior add/remove pair nets to zero either way).
                CountAdded(isDirectory);
            }
            else if (wasDirectory != isDirectory)
            {
                CountRemoved(wasDirectory);
                CountAdded(isDirectory);
            }
            Added[deltaIdx] = record;
        }
        else if (TryFindBaseRow(id, out var baseRow) && !DeletedBase.Contains(baseRow) && !RenamedAway.ContainsKey(baseRow))
        {
            var wasDirectory = BaseOverrides.TryGetValue(baseRow, out var existing) ? IsDir(existing.Flags) : Snapshot.IsDirectory(baseRow);
            if (wasDirectory != isDirectory) { CountRemoved(wasDirectory); CountAdded(isDirectory); }
            BaseOverrides[baseRow] = record;
        }
        else
        {
            // Includes id REUSE after a delete (editors save via rename-away-and-back constantly): the
            // tombstoned base row must stay dead and the reused id gets a fresh delta row, exactly
            // like the old engine's TryGetIndexById-skips-deleted + append behavior.
            _addedById[id] = Added.Count;
            Added.Add(record);
            CountAdded(isDirectory);
        }
    }

    // Whether `id` currently resolves to a LIVE row -- a base row that's unmodified or overridden
    // (renamed/moved in place still counts as existing), or a non-removed Added record. Deliberately
    // NOT `!IsSuperseded(row)`: IsSuperseded also flags overridden rows (so name search's base-row
    // pass skips them in favor of the override record), but an override means "exists under new
    // attributes," not "gone." Mirrors QueryExtensions.TryGetIndexById's skip-deleted semantics; used
    // by watcher-family path application to avoid redundantly re-Upserting an already-known parent.
    public bool Exists(UInt128 id)
    {
        if (_addedById.TryGetValue(id, out var idx))
            return !Added[idx].Removed;
        return TryFindBaseRow(id, out var row) && !DeletedBase.Contains(row) && !RenamedAway.ContainsKey(row);
    }

    public void Remove(UInt128 id)
    {
        if (_addedById.TryGetValue(id, out var deltaIdx))
            RemoveAddedCascade(Added[deltaIdx]);
        // Not IsSuperseded: that also flags a row overridden (renamed/moved in place) but still fully
        // live -- guarding on it here would make a row permanently unremovable the moment it's ever
        // renamed once. IsVisiblyDeleted correctly excludes only rows that are genuinely gone.
        else if (TryFindBaseRow(id, out var baseRow) && !IsVisiblyDeleted(baseRow))
            TombstoneCascade(baseRow);
    }

    // A base row no longer visible under its snapshot identity: deleted, renamed away (USN old-name
    // record), or represented by its override record (which the delta pass matches under the new name).
    // The usual state: nothing has been deleted, renamed away or overridden since the snapshot was
    // written. Lets a scan over many rows skip IsSuperseded's three hash lookups per row outright
    // rather than performing them to learn that every set is empty.
    public bool HasNoBaseChanges => DeletedBase.Count == 0 && RenamedAway.Count == 0 && BaseOverrides.Count == 0;

    public bool IsSuperseded(int baseRow) => DeletedBase.Contains(baseRow) || RenamedAway.ContainsKey(baseRow) || BaseOverrides.ContainsKey(baseRow);

    // Gone from visibility under its base identity: hard-deleted, or renamed away (the new identity
    // lives in a delta row). Overridden rows are NOT in this set -- they stay visible with patched values.
    public bool IsVisiblyDeleted(int baseRow) => DeletedBase.Contains(baseRow) || RenamedAway.ContainsKey(baseRow);

    public string NameOf(int baseRow) => BaseOverrides.TryGetValue(baseRow, out var o) ? o.Name : Snapshot.GetName(baseRow);

    internal int ParentOf(int baseRow) => BaseOverrides.TryGetValue(baseRow, out var o) ? o.ParentBaseRow : Snapshot.ParentIndexes[baseRow];

    public (long Size, uint Creation, uint LastWrite, uint LastAccess) MetadataOf(int baseRow)
    {
        if (BaseOverrides.TryGetValue(baseRow, out var o))
            return (o.Size, o.Creation, o.LastWrite, o.LastAccess);
        if (MetadataOverrides.TryGetValue(baseRow, out var m))
            return m;
        return (Snapshot.Sizes[baseRow], Snapshot.CreationTimes[baseRow], Snapshot.LastWriteTimes[baseRow], Snapshot.LastAccessTimes[baseRow]);
    }

    // Path building lives in DeltaPathBuilder (composition, purely to keep this file under the repo's
    // per-file line limit); the builder has no state of its own and always operates on this overlay.
    public string GetFullPath(int baseRow) => DeltaPathBuilder.GetFullPath(this, baseRow);
    internal string GetFullPath(DeltaRecord record) => DeltaPathBuilder.GetFullPath(this, record);
    internal string GetParentPath(DeltaRecord record) => DeltaPathBuilder.GetParentPath(this, record);

    internal DeltaRecord? FindAddedDirectory(UInt128 frn)
    {
        foreach (var record in Added)
            if (!record.Removed && record.Id == frn && (record.Flags & (ushort)FileRecordFlags.Directory) != 0)
                return record;
        return null;
    }

    // Any one live row's path for a given FRN (base or delta-added; hard links share one
    // $STANDARD_INFORMATION, so any single link's path is authoritative for a stat). Used by USN
    // metadata-refresh, which only needs ONE path to re-stat before writing the result to every row.
    internal bool TryGetPathForFrn(UInt128 frn, out string path)
    {
        if (TryFindBaseRow(frn, out var anyRow))
        {
            var ids = Snapshot.Ids;
            var first = anyRow;
            while (first > 0 && ids[first - 1] == frn)
                first--;
            for (var row = first; row < Snapshot.Count && ids[row] == frn; row++)
            {
                if (!IsSuperseded(row))
                {
                    path = GetFullPath(row);
                    return true;
                }
            }
        }
        foreach (var record in Added)
        {
            if (!record.Removed && record.Id == frn)
            {
                path = GetFullPath(record);
                return true;
            }
        }
        path = string.Empty;
        return false;
    }

    internal bool TryFindLiveBaseDirectory(UInt128 frn, out int row)
    {
        if (TryFindBaseRow(frn, out var anyRow))
        {
            var ids = Snapshot.Ids;
            var first = anyRow;
            while (first > 0 && ids[first - 1] == frn)
                first--;
            for (row = first; row < Snapshot.Count && ids[row] == frn; row++)
                if (!IsSuperseded(row) && Snapshot.IsDirectory(row))
                    return true;
        }
        row = -1;
        return false;
    }

    // Mirrors HardLinkDelta.Matches' parent term for appended rows: an unhealed orphan (parent never
    // indexed) has ParentIndexes=-1 in the old engine, so its computed parent FRN is 0 and NO triple
    // can match it until the parent appears. A stored ParentFrn only counts as matchable while it
    // actually resolves to a live row.
    internal bool ParentResolves(DeltaRecord record)
    {
        if (record.ParentBaseRow >= 0 && !DeletedBase.Contains(record.ParentBaseRow) && !RenamedAway.ContainsKey(record.ParentBaseRow))
            return true;
        return TryFindLiveBaseDirectory(record.ParentFrn, out _) || FindAddedDirectory(record.ParentFrn) != null;
    }

    // Rows the delta pass must match by name: overrides (renamed base rows) + live added rows.
    internal IEnumerable<DeltaRecord> RowsToMatch()
    {
        foreach (var record in BaseOverrides.Values)
            yield return record;
        foreach (var record in Added)
            if (!record.Removed)
                yield return record;
    }

    internal bool TryFindBaseRow(UInt128 id, out int row)
    {
        row = Snapshot.FirstRowForId(id);
        return row >= 0;
    }

    private int ResolveParentRow(UInt128 id, UInt128 parentId)
    {
        if (parentId == id)
            return -1;
        return TryFindBaseRow(parentId, out var row) ? row : -1;
    }

    // Directory-delete cascades live in DeltaCascade (split to keep this file under the line limit).
    internal void TombstoneCascade(int baseRow) => DeltaCascade.Tombstone(this, baseRow);
    internal void RemoveAddedCascade(DeltaRecord record) => DeltaCascade.RemoveAdded(this, record);
}
