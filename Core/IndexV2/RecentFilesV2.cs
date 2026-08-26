using Lertaro.PluginSdk.Abstractions;

using Lertaro.Core.IndexV2.Delta;

using Lertaro.Core.IndexV2.Search;

using Lertaro.Core.IndexV2.Persistence;
namespace Lertaro.Core.IndexV2;

// Mirrors RecentFilesWalker.CollectFromDirectory over Snapshot+DeltaOverlay: an in-memory subtree DFS
// via the CSR children column, base rows plus delta rows relocated into the walked subtree, capped at
// the same per-directory scan limit so a target accidentally set to a whole drive can't runaway.
public static class RecentFilesV2
{
    public const int MaxScannedPerDirectory = 200_000;

    public static void CollectFromDirectory(Snapshot snapshot, DeltaOverlay delta, string dirLower, string drive, uint cutoffUtc, List<SearchResult> candidates)
    {
        if (!DirectoryFilterResolver.TryResolve(snapshot, delta, dirLower, forceLastSegmentAsQuery: false, out var rootRow, out var remainder)
            || remainder.Length > 0 || !snapshot.IsDirectory(rootRow) || delta.IsSuperseded(rootRow))
            return;

        var stack = new Stack<int>();
        stack.Push(rootRow);
        var scanned = 0;
        while (stack.Count > 0 && scanned < MaxScannedPerDirectory)
        {
            var current = stack.Pop();
            foreach (var child in snapshot.ChildrenOf(current))
            {
                if (snapshot.IsDeleted(child) || delta.IsSuperseded(child))
                    continue;
                scanned++;
                if (snapshot.IsDirectory(child))
                    stack.Push(child);
                Emit(snapshot, delta, child, drive, cutoffUtc, candidates);
            }
            foreach (var (row, record) in delta.BaseOverrides)
            {
                if (record.ParentBaseRow != current)
                    continue;
                scanned++;
                if ((record.Flags & (ushort)FileRecordFlags.Directory) != 0)
                    stack.Push(row);
                EmitOverride(delta, row, record, drive, cutoffUtc, candidates);
            }
            var currentFrn = snapshot.Ids[current];
            foreach (var record in delta.Added)
            {
                if (record.Removed || record.ParentFrn != currentFrn)
                    continue;
                scanned++;
                var isDirectory = (record.Flags & (ushort)FileRecordFlags.Directory) != 0;
                var flags = (FileRecordFlags)record.Flags;
                if (!isDirectory && record.LastWrite >= cutoffUtc && !IsHiddenOrSystem(flags))
                    candidates.Add(ToResult(record.Name, delta.GetFullPath(record), flags, drive, record.LastWrite));
            }
        }
    }

    /// <summary>Files only: a directory is walked into, never offered as a result of its own.</summary>
    /// <remarks>
    /// Every caller of this asks for recent FILES -- the quick panel source kind of that name, and the
    /// startup panel's tab of that name. A folder's own modified time changes whenever anything is added
    /// to or removed from it, so including them did not merely add noise: they were among the newest
    /// things in any active folder and took the top of the list, pushing out the files the list exists
    /// to show.
    ///
    /// Filtered here rather than after collecting, so the caller's cap counts what it thinks it counts:
    /// both GetRecentFiles extensions sort and then Take(limit), and a set half full of folders would
    /// have left "at most 20" showing however many of those 20 happened to be files.
    /// </remarks>
    private static void Emit(Snapshot snapshot, DeltaOverlay delta, int row, string drive, uint cutoffUtc, List<SearchResult> candidates)
    {
        if (snapshot.IsDirectory(row))
            return;
        var flags = delta.BaseOverrides.TryGetValue(row, out var overridden)
            ? (FileRecordFlags)overridden.Flags
            : (FileRecordFlags)snapshot.Flags[row];
        if (IsHiddenOrSystem(flags))
            return;
        var (_, _, lastWrite, _) = delta.MetadataOf(row);
        if (lastWrite < cutoffUtc)
            return;
        candidates.Add(ToResult(delta.NameOf(row), delta.GetFullPath(row), flags, drive, lastWrite));
    }

    private static void EmitOverride(DeltaOverlay delta, int row, DeltaOverlay.DeltaRecord record, string drive, uint cutoffUtc, List<SearchResult> candidates)
    {
        if ((record.Flags & (ushort)FileRecordFlags.Directory) != 0)
            return;
        var flags = (FileRecordFlags)record.Flags;
        if (IsHiddenOrSystem(flags))
            return;
        if (record.LastWrite < cutoffUtc)
            return;
        candidates.Add(ToResult(record.Name, delta.GetFullPath(row), flags, drive, record.LastWrite));
    }

    private static bool IsHiddenOrSystem(FileRecordFlags flags)
        => (flags & (FileRecordFlags.Hidden | FileRecordFlags.System)) != 0;

    // Size/Created/Accessed aren't tracked here -- Recent Files only ever needs Modified, for the
    // recency merge/sort (see SearchEngineRecentFilesExtensions/SearchService.GetRecentFilesAsync).
    private static SearchResult ToResult(string name, string path, FileRecordFlags flags, string drive, uint modifiedUtc) => new()
    {
        Name = name,
        Path = path,
        IsDir = (flags & FileRecordFlags.Directory) != 0,
        Drive = drive,
        Attributes = FileRecordFlagsHelper.ToAttributes(flags),
        Metadata = new FileMetadata(0, DateTime.MinValue, FileTimeHelper.FromUnixSeconds(modifiedUtc).ToLocalTime(), DateTime.MinValue),
    };
}
