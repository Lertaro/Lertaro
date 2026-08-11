using Lertaro.Core.IndexV2.Alias;

namespace Lertaro.Core.IndexV2.Delta;

// Per-link delta operations mirroring HardLinkDelta: keyed by the (FRN, parentFRN, name) triple a USN
// record carries, so one hard link's create/delete never disturbs the file's other links. AddLink
// tolerates an unresolved parent (out-of-order arrival) -- DeltaOverlay.GetFullPath re-resolves the
// stashed parent FRN lazily, healing the moment the parent's own row appears (the old engine does the
// same eagerly via ReparentWaitingOrphans).
public static class DeltaLinkOps
{
    private const FileRecordFlags IndexOnlyFlags = FileRecordFlags.SourceRoot | FileRecordFlags.Listed;

    public static void AddLink(DeltaOverlay delta, UInt128 frn, UInt128 parentFrn, string name, FileRecordFlags flags)
    {
        if (FindMatchingBaseRow(delta, frn, parentFrn, name) >= 0 || FindMatchingAdded(delta, frn, parentFrn, name) != null)
            return; // this exact link is already present

        var record = new DeltaOverlay.DeltaRecord
        {
            Id = frn,
            Name = name,
            Flags = (ushort)flags,
            ParentBaseRow = delta.TryFindBaseRow(parentFrn, out var parentRow) ? parentRow : -1,
            ParentFrn = parentFrn,
        };
        record.Aliases = AliasGeneration.Generate(name, out var providerIds);
        record.ProviderIds = providerIds;
        delta.Added.Add(record);
        delta.CountAdded(flags.HasFlag(FileRecordFlags.Directory));
    }

    public static void RemoveLink(DeltaOverlay delta, UInt128 frn, UInt128 parentFrn, string name)
    {
        var baseRow = FindMatchingBaseRow(delta, frn, parentFrn, name);
        if (baseRow >= 0)
        {
            delta.TombstoneCascade(baseRow);
            return;
        }
        var added = FindMatchingAdded(delta, frn, parentFrn, name);
        if (added != null)
            delta.RemoveAddedCascade(added);
    }

    // RENAME_OLD_NAME: the FRN survives under a new name, so children must NOT cascade -- the row is
    // marked renamed-away and path walks forward through the FRN once the new-name AddLink arrives
    // (mirrors HardLinkDelta.RemoveLinkForRename + ReorphanChildren). Counted as removed immediately,
    // like the old engine's MarkRowDeleted -- AddLink re-counts it when the new-name row lands, so a
    // mid-rename status poll sees a brief -1/+1 dip, matching RuntimeIndex's own behavior exactly.
    public static void RemoveLinkForRename(DeltaOverlay delta, UInt128 frn, UInt128 parentFrn, string name)
    {
        var baseRow = FindMatchingBaseRow(delta, frn, parentFrn, name);
        if (baseRow >= 0)
        {
            var wasDirectory = delta.BaseOverrides.TryGetValue(baseRow, out var o) ? (o.Flags & (ushort)FileRecordFlags.Directory) != 0 : delta.Snapshot.IsDirectory(baseRow);
            delta.CountRemoved(wasDirectory);
            delta.RenamedAway[baseRow] = frn;
            delta.BaseOverrides.Remove(baseRow);
            return;
        }
        var added = FindMatchingAdded(delta, frn, parentFrn, name);
        if (added != null)
        {
            // Children resolve their parent by FRN dynamically (see GetFullPath/GetParentPath), so
            // marking this record Removed is enough for them to fall through to the source root until
            // the next AddLink recreates a live record under the same FRN -- no separate forwarding
            // bookkeeping needed for in-session (never-yet-compacted) directories.
            added.Removed = true;
            delta.CountRemoved((added.Flags & (ushort)FileRecordFlags.Directory) != 0);
        }
    }

    // HARD_LINK_CHANGE can't say add vs remove: if this exact link exists it was removed, else added.
    // File-only in real USN streams -- directories can't be hard-linked (see DeltaOverlay's invariant).
    public static void ToggleLink(DeltaOverlay delta, UInt128 frn, UInt128 parentFrn, string name, FileRecordFlags flags)
    {
        var baseRow = FindMatchingBaseRow(delta, frn, parentFrn, name);
        if (baseRow >= 0)
        {
            delta.TombstoneCascade(baseRow);
            return;
        }
        var added = FindMatchingAdded(delta, frn, parentFrn, name);
        if (added != null)
        {
            delta.RemoveAddedCascade(added);
            return;
        }
        AddLink(delta, frn, parentFrn, name, flags);
    }

    // Per-FRN metadata refresh, mirroring UsnIndexerExtensions.RefreshMetadata: hard links share one
    // $STANDARD_INFORMATION, so a single stat is authoritative for EVERY row of the FRN -- live base
    // rows get a metadata overlay, override/added records are patched in place.
    public static void UpdateMetadata(DeltaOverlay delta, UInt128 frn, long size, uint creation, uint lastWrite, uint lastAccess)
    {
        var snapshot = delta.Snapshot;
        if (delta.TryFindBaseRow(frn, out var anyRow))
        {
            var ids = snapshot.Ids;
            var first = anyRow;
            while (first > 0 && ids[first - 1] == frn)
                first--;
            for (var row = first; row < snapshot.Count && ids[row] == frn; row++)
            {
                if (delta.DeletedBase.Contains(row) || delta.RenamedAway.ContainsKey(row))
                    continue;
                if (delta.BaseOverrides.TryGetValue(row, out var overrideRecord))
                {
                    overrideRecord.Size = size;
                    overrideRecord.Creation = creation;
                    overrideRecord.LastWrite = lastWrite;
                    overrideRecord.LastAccess = lastAccess;
                }
                else
                {
                    delta.MetadataOverrides[row] = (size, creation, lastWrite, lastAccess);
                }
            }
        }

        foreach (var record in delta.Added)
        {
            if (!record.Removed && record.Id == frn)
            {
                record.Size = size;
                record.Creation = creation;
                record.LastWrite = lastWrite;
                record.LastAccess = lastAccess;
            }
        }
    }

    // USN basic-information records carry current file attributes but no sizes or timestamps. Keep
    // those attributes in a full row override so every existing search path sees them immediately and
    // the next compaction persists them. SourceRoot/Listed are index bookkeeping bits absent from the
    // filesystem attributes, so they must survive the replacement.
    public static void UpdateFlags(DeltaOverlay delta, UInt128 frn, FileRecordFlags observedFlags)
    {
        var snapshot = delta.Snapshot;
        if (delta.TryFindBaseRow(frn, out var anyRow))
        {
            var ids = snapshot.Ids;
            var first = anyRow;
            while (first > 0 && ids[first - 1] == frn)
                first--;

            for (var row = first; row < snapshot.Count && ids[row] == frn; row++)
            {
                if (delta.DeletedBase.Contains(row) || delta.RenamedAway.ContainsKey(row))
                    continue;
                if (delta.BaseOverrides.TryGetValue(row, out var existing))
                {
                    existing.Flags = MergeFlags(existing.Flags, observedFlags);
                    continue;
                }

                var name = snapshot.GetName(row);
                var (size, creation, lastWrite, lastAccess) = delta.MetadataOf(row);
                var record = new DeltaOverlay.DeltaRecord
                {
                    Id = frn,
                    Name = name,
                    Flags = MergeFlags(snapshot.Flags[row], observedFlags),
                    Size = size,
                    Creation = creation,
                    LastWrite = lastWrite,
                    LastAccess = lastAccess,
                    ParentBaseRow = snapshot.ParentIndexes[row],
                    ParentFrn = snapshot.GetParentId(row),
                };
                record.Aliases = AliasGeneration.Generate(name, out var providerIds);
                record.ProviderIds = providerIds;
                delta.MetadataOverrides.Remove(row);
                delta.BaseOverrides[row] = record;
            }
        }

        foreach (var record in delta.Added)
            if (!record.Removed && record.Id == frn)
                record.Flags = MergeFlags(record.Flags, observedFlags);
    }

    private static ushort MergeFlags(ushort currentFlags, FileRecordFlags observedFlags)
        => (ushort)(observedFlags | ((FileRecordFlags)currentFlags & IndexOnlyFlags));

    // Base rows for an FRN sit adjacent (ids are sorted); match the exact link by parent FRN + name,
    // same comparison HardLinkDelta.Matches uses.
    private static int FindMatchingBaseRow(DeltaOverlay delta, UInt128 frn, UInt128 parentFrn, string name)
    {
        var snapshot = delta.Snapshot;
        if (!delta.TryFindBaseRow(frn, out var anyRow))
            return -1;

        var ids = snapshot.Ids;
        var first = anyRow;
        while (first > 0 && ids[first - 1] == frn)
            first--;

        for (var row = first; row < snapshot.Count && ids[row] == frn; row++)
        {
            if (delta.DeletedBase.Contains(row) || delta.RenamedAway.ContainsKey(row))
                continue;
            if (delta.BaseOverrides.TryGetValue(row, out var overridden))
            {
                if (overridden.ParentFrn == parentFrn && string.Equals(overridden.Name, name, StringComparison.OrdinalIgnoreCase))
                    return row;
                continue;
            }
            var parentIndex = snapshot.ParentIndexes[row];
            var rowParentFrn = parentIndex >= 0 ? ids[parentIndex] : default;
            if (rowParentFrn == parentFrn && string.Equals(snapshot.GetName(row), name, StringComparison.OrdinalIgnoreCase))
                return row;
        }
        return -1;
    }

    private static DeltaOverlay.DeltaRecord? FindMatchingAdded(DeltaOverlay delta, UInt128 frn, UInt128 parentFrn, string name)
    {
        foreach (var record in delta.Added)
        {
            if (!record.Removed && record.Id == frn && record.ParentFrn == parentFrn
                && string.Equals(record.Name, name, StringComparison.OrdinalIgnoreCase)
                && delta.ParentResolves(record)) // an unhealed orphan is triple-unmatchable, like the old engine's pf=0
                return record;
        }
        return null;
    }
}
