using Lertaro.Core.IndexV2.Delta;

using Lertaro.Core.IndexV2.Persistence;
namespace Lertaro.Core.IndexV2.Search;

// Shared "is this row under directory X" resolution for name search, path search and the recent-files
// walk. Mirrors Helpers.NormalizeFilter/TryGetDirectoryRootId/IsUnderDirectoryCached and
// PathQueryExtensions.TryResolvePath, retargeted at Snapshot+DeltaOverlay.
internal static class DirectoryFilterResolver
{
    public static string? NormalizeFilter(string? directoryFilter)
    {
        if (string.IsNullOrWhiteSpace(directoryFilter))
            return null;
        var value = directoryFilter.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar).ToLowerInvariant();
        return value.EndsWith(Path.DirectorySeparatorChar) ? value : value + Path.DirectorySeparatorChar;
    }

    public static bool ExcludesSource(Snapshot snapshot, string? directoryFilterLower)
    {
        if (directoryFilterLower == null || directoryFilterLower.Length < 3)
            return false;
        if (char.IsLetter(directoryFilterLower[0]) && directoryFilterLower[1] == Path.VolumeSeparatorChar && directoryFilterLower[2] == Path.DirectorySeparatorChar)
            return !directoryFilterLower[0].ToString().Equals(snapshot.SourceKey, StringComparison.OrdinalIgnoreCase);
        return false;
    }

    // Walks lowercase segments from the source root through child directories (base+delta aware).
    // Stops at the deepest resolvable directory; remainder holds whatever segment didn't resolve
    // (used as a name-prefix filter by exact-path navigation).
    public static bool TryResolve(Snapshot snapshot, DeltaOverlay? delta, string pathLower, bool forceLastSegmentAsQuery, out int row, out string remainder)
    {
        row = -1;
        remainder = string.Empty;
        var sourceRootLower = snapshot.SourceRoot.ToLowerInvariant();
        if (!pathLower.StartsWith(sourceRootLower, StringComparison.Ordinal))
            return false;

        var current = FindRootRow(snapshot);
        if (current < 0)
            return false;

        var start = sourceRootLower.Length;
        while (start < pathLower.Length)
        {
            var sep = pathLower.IndexOf(Path.DirectorySeparatorChar, start);
            var isLast = sep < 0;
            var segment = isLast ? pathLower.Substring(start) : pathLower.Substring(start, sep - start);
            if (segment.Length == 0)
            {
                start = sep + 1;
                continue;
            }
            if (isLast && forceLastSegmentAsQuery)
            {
                remainder = segment;
                break;
            }
            if (!TryFindChildDirectory(snapshot, delta, current, segment, out var child))
            {
                remainder = segment;
                break;
            }
            current = child;
            if (isLast)
                break;
            start = sep + 1;
        }

        row = current;
        return true;
    }

    // Ancestor check over base parentage only (mirrors Helpers.IsUnderDirectoryCached): correct for
    // rows the delta hasn't reparented, which is what direction filters check in practice; a row
    // whose delta override moved it elsewhere is handled by its caller checking BaseOverrides first.
    public static bool IsUnderCached(Snapshot snapshot, int row, int ancestorRow, Dictionary<int, bool> cache)
    {
        var stack = new List<int>();
        var current = row;
        var found = false;
        while (current >= 0)
        {
            if (cache.TryGetValue(current, out var cached))
            {
                found = cached;
                break;
            }
            if (current == ancestorRow)
            {
                found = true;
                break;
            }
            stack.Add(current);
            var parent = snapshot.ParentIndexes[current];
            if (parent == current)
                break;
            current = parent;
        }
        foreach (var idx in stack)
            cache[idx] = found;
        return found;
    }

    private static int FindRootRow(Snapshot snapshot)
    {
        for (var row = 0; row < snapshot.Count; row++)
            if ((snapshot.Flags[row] & (ushort)FileRecordFlags.SourceRoot) != 0)
                return row;
        return -1;
    }

    private static bool TryFindChildDirectory(Snapshot snapshot, DeltaOverlay? delta, int parentRow, string nameLower, out int childRow)
    {
        foreach (var child in snapshot.ChildrenOf(parentRow))
        {
            if (delta != null && delta.IsSuperseded(child))
                continue;
            if (!snapshot.IsDeleted(child) && snapshot.IsDirectory(child)
                && snapshot.GetName(child).Equals(nameLower, StringComparison.OrdinalIgnoreCase))
            {
                childRow = child;
                return true;
            }
        }
        childRow = -1;
        return false;
    }
}
