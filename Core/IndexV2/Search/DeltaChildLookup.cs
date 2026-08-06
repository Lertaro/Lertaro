using Lertaro.Core.IndexV2.Delta;

using Lertaro.Core.IndexV2.Persistence;
namespace Lertaro.Core.IndexV2.Search;

// The overlay's rows grouped by the directory they currently live in -- the half of "list this
// directory's children" that Snapshot.ChildrenOf's frozen CSR cannot answer (renamed/moved-in rows and
// brand-new ones). Built once per enumeration and reused for every level, so a subtree walk never
// rescans BaseOverrides/Added per directory. Split out of DirectoryEnumerator purely to keep each file
// under the repo's per-file line limit; it holds no state beyond the two maps it hands back.
//
// Entry indices use ResultBuilder's convention: < Snapshot.Count addresses a base row (here always one
// carrying an override record), >= Snapshot.Count addresses DeltaOverlay.Added[entry - Count].
internal sealed class DeltaChildLookup
{
    private static readonly List<int> None = new();

    private readonly Dictionary<int, List<int>> _byParentRow = new();
    private readonly Dictionary<UInt128, List<int>> _byParentFrn = new();

    // null when the overlay has nothing to contribute -- the common case (no USN traffic since the last
    // compaction), and worth a null check per level to skip two dictionary lookups each.
    public static DeltaChildLookup? Build(Snapshot snapshot, DeltaOverlay delta)
    {
        if (delta.BaseOverrides.Count == 0 && delta.Added.Count == 0)
            return null;

        // Added directories by FRN, so parent resolution below stays O(1) per record instead of
        // DeltaOverlay.FindAddedDirectory's linear scan (which would make this build quadratic in the
        // number of pending additions -- routine while a large copy is landing).
        var addedDirs = new HashSet<UInt128>();
        foreach (var record in delta.Added)
        {
            if (!record.Removed && (record.Flags & (ushort)FileRecordFlags.Directory) != 0)
                addedDirs.Add(record.Id);
        }

        var lookup = new DeltaChildLookup();
        foreach (var (row, record) in delta.BaseOverrides)
        {
            // An overridden row that was tombstoned afterwards keeps its override record around
            // (DeltaCascade.Tombstone only removes it from BaseOverrides on the row it starts at).
            if (!delta.IsVisiblyDeleted(row))
                lookup.Add(delta, addedDirs, row, record);
        }
        for (var i = 0; i < delta.Added.Count; i++)
        {
            var record = delta.Added[i];
            if (!record.Removed)
                lookup.Add(delta, addedDirs, snapshot.Count + i, record);
        }
        return lookup;
    }

    public List<int> ChildrenOfRow(int row) => _byParentRow.TryGetValue(row, out var list) ? list : None;

    public List<int> ChildrenOfFrn(UInt128 frn) => _byParentFrn.TryGetValue(frn, out var list) ? list : None;

    private void Add(DeltaOverlay delta, HashSet<UInt128> addedDirs, int entry, DeltaOverlay.DeltaRecord record)
    {
        // Deliberately the same resolution order as DeltaOverlay.GetParentPath: an entry must be listed
        // under exactly the directory whose path its own result carries, or enumerating that directory
        // would return a row claiming to live somewhere else (or miss one that claims to live here).
        if (record.ParentBaseRow >= 0 && !delta.IsVisiblyDeleted(record.ParentBaseRow))
        {
            Append(_byParentRow, record.ParentBaseRow, entry);
            return;
        }
        if (delta.TryFindLiveBaseDirectory(record.ParentFrn, out var baseRow))
        {
            Append(_byParentRow, baseRow, entry);
            return;
        }
        if (addedDirs.Contains(record.ParentFrn))
            Append(_byParentFrn, record.ParentFrn, entry);
        // Else the parent doesn't resolve to any live directory yet (an out-of-order USN arrival whose
        // parent record hasn't been applied): unreachable from every directory, exactly like the rows
        // DeltaOverlay.ParentResolves already reports as unmatchable. It becomes reachable on its own
        // as soon as the parent lands, since this lookup is rebuilt per enumeration.
    }

    private static void Append<TKey>(Dictionary<TKey, List<int>> map, TKey key, int entry) where TKey : notnull
    {
        if (!map.TryGetValue(key, out var list))
            map[key] = list = new List<int>();
        list.Add(entry);
    }
}
