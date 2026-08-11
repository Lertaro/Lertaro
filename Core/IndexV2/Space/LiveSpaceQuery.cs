using System.Runtime.CompilerServices;
using Lertaro.Core.IndexV2.Delta;
using Lertaro.Core.IndexV2.Persistence;
using Lertaro.Core.IndexV2.Search;

namespace Lertaro.Core.IndexV2.Space;

/// <summary>Builds a directory-level space view from an already loaded LiveIndex.</summary>
internal static class LiveSpaceQuery
{
    private const string RootCacheKey = "\0";
    private static readonly ConditionalWeakTable<LiveIndex, SpaceQueryCache> Caches = new();

    public static SpaceQueryResult GetEntries(LiveIndex live, string? directory)
    {
        var revision = live.Revision;
        var key = string.IsNullOrWhiteSpace(directory) ? RootCacheKey : NormalizePath(directory);
        var cache = Caches.GetValue(live, static _ => new SpaceQueryCache());
        if (cache.TryGet(revision, key, out var cached))
            return cached;

        var result = live.Read((snapshot, delta) => Query(snapshot, delta, key));
        if (live.Revision == revision)
            cache.Store(revision, key, result);
        return result;
    }

    private static SpaceQueryResult Query(Snapshot snapshot, DeltaOverlay delta, string key)
    {
        var root = snapshot.FirstRowForId(snapshot.RootId);
        if (root < 0)
            return SpaceQueryResult.NotFound;

        var lookup = DeltaChildLookup.Build(snapshot, delta);
        var affected = BuildAffectedEntries(snapshot, delta, out var canonicalAddedLinks);
        if (key == RootCacheKey)
        {
            var rootEntry = CreateEntry(snapshot, delta, lookup, affected, canonicalAddedLinks, root, snapshot.SourceRoot);
            return new SpaceQueryResult(true, [rootEntry]);
        }
        if (!TryResolve(snapshot, delta, lookup, root, key, out var directory))
            return SpaceQueryResult.NotFound;
        var children = CollectChildren(snapshot, delta, lookup, directory);
        var entries = new List<SpaceIndexEntry>(children.Count);
        foreach (var child in children)
        {
            if (IsHiddenOrSystem(snapshot, delta, child))
                continue;
            entries.Add(CreateEntry(snapshot, delta, lookup, affected, canonicalAddedLinks, child, GetName(snapshot, delta, child)));
        }
        entries.Sort(CompareEntries);
        return new SpaceQueryResult(true, entries);
    }

    private static SpaceIndexEntry CreateEntry(Snapshot snapshot, DeltaOverlay delta, DeltaChildLookup? lookup,
        HashSet<int> affected, Dictionary<UInt128, int> canonicalAddedLinks, int entry, string name)
    {
        var size = GetRecursiveSize(snapshot, delta, lookup, affected, canonicalAddedLinks, entry);
        var isDirectory = IsDirectory(snapshot, delta, entry);
        var rawSize = isDirectory ? 0 : GetRawSize(snapshot, delta, entry);
        return new SpaceIndexEntry(
            GetPath(snapshot, delta, entry),
            name,
            Math.Max(0, size),
            isDirectory,
            !isDirectory && rawSize > 0 && size == 0);
    }

    private static long GetRecursiveSize(Snapshot snapshot, DeltaOverlay delta, DeltaChildLookup? lookup,
        HashSet<int> affected, Dictionary<UInt128, int> canonicalAddedLinks, int entry, int depth = 0)
    {
        if (depth >= 512)
            return 0;
        if (!IsDirectory(snapshot, delta, entry))
            return IsCanonicalFileLink(snapshot, delta, canonicalAddedLinks, entry) ? Math.Max(0, GetRawSize(snapshot, delta, entry)) : 0;
        if (entry < snapshot.Count && !affected.Contains(entry))
            return Math.Max(0, snapshot.RecursiveSizes[entry]);
        var total = 0L;
        foreach (var child in CollectChildren(snapshot, delta, lookup, entry))
            total = SaturatingAdd(total, GetRecursiveSize(snapshot, delta, lookup, affected, canonicalAddedLinks, child, depth + 1));
        return total;
    }

    private static HashSet<int> BuildAffectedEntries(Snapshot snapshot, DeltaOverlay delta,
        out Dictionary<UInt128, int> canonicalAddedLinks)
    {
        var affected = new HashSet<int>();
        var addedDirectories = new Dictionary<UInt128, int>();
        var addedById = new Dictionary<UInt128, List<int>>();
        canonicalAddedLinks = new Dictionary<UInt128, int>();
        for (var index = 0; index < delta.Added.Count; index++)
        {
            var record = delta.Added[index];
            if (record.Removed)
                continue;
            var entry = snapshot.Count + index;
            if (!addedById.TryGetValue(record.Id, out var links))
                addedById[record.Id] = links = [];
            links.Add(entry);
            canonicalAddedLinks.TryAdd(record.Id, entry);
            if ((record.Flags & (ushort)FileRecordFlags.Directory) != 0)
                addedDirectories.TryAdd(record.Id, entry);
        }
        foreach (var row in delta.DeletedBase)
        {
            MarkBaseAncestors(snapshot, row, affected);
            MarkLinkPeerAncestors(snapshot, delta, addedDirectories, addedById, snapshot.Ids[row], affected);
        }
        foreach (var row in delta.RenamedAway.Keys)
            MarkBaseAncestors(snapshot, row, affected);
        foreach (var row in delta.MetadataOverrides.Keys)
            MarkBaseAncestors(snapshot, row, affected);
        foreach (var (row, record) in delta.BaseOverrides)
        {
            MarkBaseAncestors(snapshot, row, affected);
            MarkCurrentAncestors(snapshot, delta, addedDirectories, record, affected);
        }
        for (var index = 0; index < delta.Added.Count; index++)
        {
            var record = delta.Added[index];
            affected.Add(snapshot.Count + index);
            MarkCurrentAncestors(snapshot, delta, addedDirectories, record, affected);
            if (record.Removed)
                MarkLinkPeerAncestors(snapshot, delta, addedDirectories, addedById, record.Id, affected);
        }
        return affected;
    }

    private static void MarkLinkPeerAncestors(Snapshot snapshot, DeltaOverlay delta,
        Dictionary<UInt128, int> addedDirectories, Dictionary<UInt128, List<int>> addedById,
        UInt128 id, HashSet<int> affected)
    {
        var first = snapshot.FirstRowForId(id);
        if (first >= 0)
            for (var row = first; row < snapshot.Count && snapshot.Ids[row] == id; row++)
                if (!delta.IsVisiblyDeleted(row))
                    MarkBaseAncestors(snapshot, row, affected);
        if (!addedById.TryGetValue(id, out var links))
            return;
        foreach (var entry in links)
            MarkCurrentAncestors(snapshot, delta, addedDirectories, delta.Added[entry - snapshot.Count], affected);
    }

    private static void MarkCurrentAncestors(Snapshot snapshot, DeltaOverlay delta,
        Dictionary<UInt128, int> addedDirectories, DeltaOverlay.DeltaRecord record, HashSet<int> affected)
    {
        if (record.ParentBaseRow >= 0 && !delta.IsVisiblyDeleted(record.ParentBaseRow))
        {
            MarkBaseAncestors(snapshot, record.ParentBaseRow, affected);
            return;
        }
        if (delta.TryFindLiveBaseDirectory(record.ParentFrn, out var parent))
        {
            MarkBaseAncestors(snapshot, parent, affected);
            return;
        }
        if (addedDirectories.TryGetValue(record.ParentFrn, out var entry) && affected.Add(entry))
        {
            MarkCurrentAncestors(snapshot, delta, addedDirectories, delta.Added[entry - snapshot.Count], affected);
            return;
        }
    }

    private static void MarkBaseAncestors(Snapshot snapshot, int row, HashSet<int> affected)
    {
        for (var depth = 0; depth < 512 && (uint)row < (uint)snapshot.Count && affected.Add(row); depth++)
        {
            var parent = snapshot.ParentIndexes[row];
            if (parent < 0 || parent == row)
                break;
            row = parent;
        }
    }

    private static bool TryResolve(Snapshot snapshot, DeltaOverlay delta, DeltaChildLookup? lookup,
        int root, string path, out int directory)
    {
        directory = root;
        var sourceRoot = NormalizePath(snapshot.SourceRoot);
        if (!path.StartsWith(sourceRoot, StringComparison.OrdinalIgnoreCase))
            return false;
        var remainder = path[sourceRoot.Length..].Trim(Path.DirectorySeparatorChar);
        if (remainder.Length == 0)
            return true;
        foreach (var segment in remainder.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            var found = -1;
            foreach (var child in CollectChildren(snapshot, delta, lookup, directory))
            {
                if (IsDirectory(snapshot, delta, child)
                    && GetName(snapshot, delta, child).Equals(segment, StringComparison.OrdinalIgnoreCase))
                {
                    found = child;
                    break;
                }
            }
            if (found < 0)
                return false;
            directory = found;
        }
        return true;
    }

    private static List<int> CollectChildren(Snapshot snapshot, DeltaOverlay delta, DeltaChildLookup? lookup, int entry)
    {
        var result = new List<int>();
        if (entry < snapshot.Count)
        {
            AppendBaseChildren(snapshot, delta, entry, result);
            if (lookup != null)
                result.AddRange(lookup.ChildrenOfRow(entry));
        }
        else
        {
            var record = delta.Added[entry - snapshot.Count];
            foreach (var (oldRow, frn) in delta.RenamedAway)
                if (frn == record.Id)
                    AppendBaseChildren(snapshot, delta, oldRow, result);
            if (lookup != null)
                result.AddRange(lookup.ChildrenOfFrn(record.Id));
        }
        return result;
    }

    private static void AppendBaseChildren(Snapshot snapshot, DeltaOverlay delta, int parent, List<int> result)
    {
        foreach (var child in snapshot.ChildrenOf(parent))
            if (!snapshot.IsDeleted(child) && !delta.IsSuperseded(child))
                result.Add(child);
    }

    private static bool IsCanonicalFileLink(Snapshot snapshot, DeltaOverlay delta,
        Dictionary<UInt128, int> canonicalAddedLinks, int entry)
    {
        var id = entry < snapshot.Count ? snapshot.Ids[entry] : delta.Added[entry - snapshot.Count].Id;
        var first = snapshot.FirstRowForId(id);
        if (first >= 0)
        {
            for (var row = first; row < snapshot.Count && snapshot.Ids[row] == id; row++)
                if (!delta.IsVisiblyDeleted(row))
                    return entry == row;
        }
        return canonicalAddedLinks.TryGetValue(id, out var canonical) && entry == canonical;
    }

    private static string GetName(Snapshot snapshot, DeltaOverlay delta, int entry)
        => entry < snapshot.Count ? delta.NameOf(entry) : delta.Added[entry - snapshot.Count].Name;

    private static string GetPath(Snapshot snapshot, DeltaOverlay delta, int entry)
        => entry < snapshot.Count ? delta.GetFullPath(entry) : delta.GetFullPath(delta.Added[entry - snapshot.Count]);

    private static long GetRawSize(Snapshot snapshot, DeltaOverlay delta, int entry)
        => entry < snapshot.Count ? delta.MetadataOf(entry).Size : delta.Added[entry - snapshot.Count].Size;

    private static bool IsDirectory(Snapshot snapshot, DeltaOverlay delta, int entry)
        => (GetFlags(snapshot, delta, entry) & (ushort)FileRecordFlags.Directory) != 0;

    private static bool IsHiddenOrSystem(Snapshot snapshot, DeltaOverlay delta, int entry)
        => (GetFlags(snapshot, delta, entry) & (ushort)(FileRecordFlags.Hidden | FileRecordFlags.System)) != 0;

    private static ushort GetFlags(Snapshot snapshot, DeltaOverlay delta, int entry)
        => entry >= snapshot.Count ? delta.Added[entry - snapshot.Count].Flags
            : delta.BaseOverrides.TryGetValue(entry, out var record) ? record.Flags : snapshot.Flags[entry];

    private static string NormalizePath(string path)
        => path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

    private static int CompareEntries(SpaceIndexEntry left, SpaceIndexEntry right)
    {
        var bySize = right.Size.CompareTo(left.Size);
        if (bySize != 0) return bySize;
        if (left.IsDirectory != right.IsDirectory) return left.IsDirectory ? -1 : 1;
        return StringComparer.CurrentCultureIgnoreCase.Compare(left.Name, right.Name);
    }

    private static long SaturatingAdd(long left, long right)
        => left > long.MaxValue - right ? long.MaxValue : left + right;

}

internal readonly record struct SpaceQueryResult(bool Found, IReadOnlyList<SpaceIndexEntry> Entries)
{
    public static SpaceQueryResult NotFound { get; } = new(false, Array.Empty<SpaceIndexEntry>());
}
