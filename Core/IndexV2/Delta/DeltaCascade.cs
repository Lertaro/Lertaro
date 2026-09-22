namespace Lertaro.Core.IndexV2.Delta;

// Directory-delete cascades over CURRENT parentage, mirroring HardLinkDelta.CascadeDeleteChildren:
// the snapshot CSR alone goes stale the moment overrides move rows around, so both walks patch it --
// children moved out are skipped, rows moved in are included, and delta rows parented here recurse.
// Relies on the DeltaOverlay invariant: at most one live directory row per FRN (see DeltaOverlay's
// header comment) -- real USN streams guarantee it, so attributing delta children by parent FRN here
// is unambiguous.
internal static class DeltaCascade
{
    private const int MaxDepth = 512;

    internal static void Tombstone(DeltaOverlay delta, int baseRow, int depth = 0)
    {
        // The same backstop every other walk in this module uses against a parent cycle in the data
        // (Snapshot.GetFullPath, DeltaPathBuilder.GetFullPath, PathGate.Verify, LiveSpaceQuery). The
        // cycles here do terminate on their own -- record.Removed and DeletedBase are set before
        // recursing -- so what this bounds is stack depth on a legitimately deep tree, which a
        // long-path Windows directory can exceed.
        if (depth >= MaxDepth)
            return;

        // A row's true "current" flags are its override's if present, else the base snapshot's --
        // read BEFORE removing the override entry below, matching whatever visibility state made this
        // row countable in the first place (an already-superseded/renamed-away row was already
        // decounted at ITS OWN transition; this method must never run on one, see the guards below).
        var wasDirectory = delta.BaseOverrides.TryGetValue(baseRow, out var overridden)
            ? (overridden.Flags & (ushort)FileRecordFlags.Directory) != 0
            : delta.Snapshot.IsDirectory(baseRow);
        delta.CountRemoved(wasDirectory);

        delta.DeletedBase.Add(baseRow);
        delta.BaseOverrides.Remove(baseRow);
        if (!wasDirectory)
            return; // not currently a directory -- no children possible

        foreach (var child in delta.Snapshot.ChildrenOf(baseRow))
        {
            // RenamedAway children are a CLOSED identity here: their live descendants are reached
            // exclusively through their own successor's RenamedAway-forwarding walk in RemoveAdded
            // below (triggered when that successor's Added row itself gets removed) -- touching the
            // stale base row again would double-count and re-walk an already-superseded subtree.
            if (delta.DeletedBase.Contains(child) || delta.RenamedAway.ContainsKey(child))
                continue;
            if (delta.BaseOverrides.TryGetValue(child, out var moved) && moved.ParentBaseRow != baseRow)
                continue; // moved out from under this directory
            Tombstone(delta, child, depth + 1);
        }
        foreach (var (row, record) in delta.BaseOverrides.ToList())
        {
            if (record.ParentBaseRow == baseRow && !delta.DeletedBase.Contains(row))
                Tombstone(delta, row, depth + 1); // moved INTO this directory from elsewhere
        }
        var dirFrn = delta.Snapshot.Ids[baseRow];
        foreach (var record in delta.Added)
        {
            if (!record.Removed && (record.ParentBaseRow == baseRow || record.ParentFrn == dirFrn))
                RemoveAdded(delta, record, depth + 1);
        }
    }

    // Removing an ADDED directory record (e.g. the healed new-name row of a renamed directory) must
    // cascade like the old engine's CascadeDeleteChildren on the appended row: the re-parented base
    // children (reachable through the renamed-away forwarding) and any delta rows parented to it.
    internal static void RemoveAdded(DeltaOverlay delta, DeltaOverlay.DeltaRecord record, int depth = 0)
    {
        if (record.Removed || depth >= MaxDepth)
            return;
        record.Removed = true;
        delta.CountRemoved((record.Flags & (ushort)FileRecordFlags.Directory) != 0);
        if ((record.Flags & (ushort)FileRecordFlags.Directory) == 0)
            return;

        foreach (var (oldRow, frn) in delta.RenamedAway.ToList())
        {
            if (frn != record.Id)
                continue;
            foreach (var child in delta.Snapshot.ChildrenOf(oldRow))
            {
                if (delta.DeletedBase.Contains(child) || delta.RenamedAway.ContainsKey(child))
                    continue;
                if (delta.BaseOverrides.TryGetValue(child, out var moved) && moved.ParentBaseRow != oldRow)
                    continue;
                Tombstone(delta, child, depth + 1);
            }
            foreach (var (row, movedIn) in delta.BaseOverrides.ToList())
            {
                if (movedIn.ParentBaseRow == oldRow && !delta.DeletedBase.Contains(row))
                    Tombstone(delta, row, depth + 1);
            }
        }
        foreach (var other in delta.Added)
        {
            if (!other.Removed && !ReferenceEquals(other, record) && other.ParentFrn == record.Id)
                RemoveAdded(delta, other, depth + 1);
        }
    }
}
