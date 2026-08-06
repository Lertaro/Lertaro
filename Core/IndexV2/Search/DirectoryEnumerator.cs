using Lertaro.Core.IndexV2.Delta;

using Lertaro.Core.IndexV2.Persistence;

using Lertaro.Core.SearchIndex.Fzf;

using Lertaro.Core.Services.Plugin.DirectoryIndex;
namespace Lertaro.Core.IndexV2.Search;

// "List what is in this directory" answered from the index instead of the filesystem: resolve the path
// to its row, then walk Snapshot.ChildrenOf (the frozen parent->children CSR) merged with the live
// overlay, level by level. Cost is O(size of that subtree), not O(volume) -- the CSR is already a
// parent-directory dimension, it just had no recursive walker until now.
//
// Related to but not the same as PathSearch.TryDirectoryChildren, which lists ONE level as a ranked
// search result set for a path-mode query. This is a plain enumeration: no ranking, no fuzzy matching,
// an optional Win32 FILE-name filter, and it descends. Directories are returned alongside files but
// are never matched against that filter (see Visit), and neither recursion nor the hidden/system filter
// is gated by it either.
internal static class DirectoryEnumerator
{
    // False = this snapshot does not hold that path (wrong drive, or no such directory in the index),
    // so the caller can try another drive or fall back to a real filesystem walk. True = the directory
    // was found and everything it holds has been emitted (possibly nothing).
    public static bool Enumerate(Snapshot snapshot, DeltaOverlay delta, string path, bool recursive,
        string[]? patterns, int limit, Action<SearchResult> onResult, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;
        var pathLower = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar).ToLowerInvariant();
        // Trailing separator, always: TryResolve first checks the path starts with this source's ROOT,
        // which itself always ends with one (PathHelpers.BuildSourceRoot). Without it, enumerating a
        // source at its own root fails the prefix check outright -- harmless for a drive letter, where
        // the root is "c:\" and a full path always has that separator anyway, but it means a folder
        // index or UNC share ("d:\projects\projecta\", "\\server\share\") could never list its own root.
        // The resolver skips the empty last segment it produces, so nothing else changes.
        if (!pathLower.EndsWith(Path.DirectorySeparatorChar))
            pathLower += Path.DirectorySeparatorChar;
        if (!DirectoryFilterResolver.TryResolve(snapshot, delta, pathLower, forceLastSegmentAsQuery: false, out var root, out var remainder)
            || remainder.Length != 0)
            return false;
        if (delta.IsVisiblyDeleted(root) || !IsDirectory(snapshot, delta, root))
            return false;

        var lookup = DeltaChildLookup.Build(snapshot, delta);
        var emitted = 0;
        // Iterative rather than recursive: a subtree walk is unbounded in depth by nature, and the
        // limit below has to be able to stop it mid-level.
        var pending = new Stack<int>();
        var visited = new HashSet<int> { root };
        pending.Push(root);

        while (pending.Count > 0)
        {
            token.ThrowIfCancellationRequested();
            var node = pending.Pop();

            if (node < snapshot.Count)
            {
                foreach (var child in snapshot.ChildrenOf(node))
                {
                    // Superseded rows come back through the lookup instead, attributed to whichever
                    // directory they are in NOW (a renamed row is still here; a moved-out one is not).
                    if (snapshot.IsDeleted(child) || delta.IsSuperseded(child))
                        continue;
                    if (!Visit(child, snapshot.GetName(child), snapshot.Flags[child]))
                        return true;
                }
            }

            if (lookup != null)
            {
                foreach (var entry in ChildrenFromDelta(snapshot, delta, lookup, node))
                {
                    var record = RecordFor(snapshot, delta, entry);
                    if (!Visit(entry, record.Name, record.Flags))
                        return true;
                }
            }
        }
        return true;

        // Returns false once the limit is reached, i.e. "stop walking".
        bool Visit(int entry, string name, ushort flags)
        {
            var isDirectory = (flags & (ushort)FileRecordFlags.Directory) != 0;
            if (recursive && isDirectory && visited.Add(entry))
                pending.Push(entry);
            // Hidden/system entries are dropped here, the same unconditional filter every other search
            // result goes through (see SearchService.SearchStreamingAsync) -- but only the entry ITSELF:
            // a hidden directory is still descended into, because AppData is hidden, and a recursive
            // listing of a user profile that stopped there would silently lose most of what a plugin
            // enumerating it is looking for. Filtering here rather than client-side also keeps `limit`
            // counting entries the caller actually receives.
            if ((flags & (ushort)(FileRecordFlags.Hidden | FileRecordFlags.System)) != 0)
                return true;
            // A FILE pattern, the way Directory.EnumerateFiles and every FilterPattern registration in
            // this codebase already mean it: directories are always listed, never matched against it.
            // "*.mp4" is written to pick video files, and applying it to directories too would hide
            // every folder in the tree -- while a caller that wants files only drops them with one
            // IsDir check, which is not recoverable the other way round.
            if (!isDirectory && patterns != null && !FilterPatternHelper.Matches(name, patterns))
                return true;
            onResult(ResultBuilder.ToResult(snapshot, delta, new FzfRank(entry, 0, 0)));
            return limit <= 0 || ++emitted < limit;
        }
    }

    private static List<int> ChildrenFromDelta(Snapshot snapshot, DeltaOverlay delta, DeltaChildLookup lookup, int node)
        => node < snapshot.Count
            ? lookup.ChildrenOfRow(node)
            : lookup.ChildrenOfFrn(delta.Added[node - snapshot.Count].Id);

    private static DeltaOverlay.DeltaRecord RecordFor(Snapshot snapshot, DeltaOverlay delta, int entry)
        => entry >= snapshot.Count ? delta.Added[entry - snapshot.Count] : delta.BaseOverrides[entry];

    private static bool IsDirectory(Snapshot snapshot, DeltaOverlay delta, int row)
        => delta.BaseOverrides.TryGetValue(row, out var overridden)
            ? (overridden.Flags & (ushort)FileRecordFlags.Directory) != 0
            : snapshot.IsDirectory(row);
}
