using Lertaro.Core.Indexer.NetworkDrive.Walk;

namespace Lertaro.Core.IndexV2.Delta;

// Watcher-family (folder-scan / FAT drive-letter) path-based delta application, mirroring
// Indexer.Shared.PathDeltaApplier but targeting a DeltaOverlay instead of a RuntimeIndex. Ids are path
// hashes (PathHelpers.HashPath64), not FRNs -- the watcher side of the "one family per drive"
// invariant; USN drives use DeltaLinkOps instead (see DeltaOverlay's header comment).
//
// ApplyDeleted no longer needs a manual recursive subtree walk the way the old RuntimeIndex-based
// RemoveSubtree did: DeltaOverlay.Remove already cascades a directory's children (base, overridden,
// and delta-added alike) via TombstoneCascade/RemoveAddedCascade, so one Remove() call on the target
// id is enough.
public static class DeltaPathApplier
{
    public static bool ApplyCreatedOrChanged(DeltaOverlay delta, UInt128 rootId, string root, string path, ExclusionRuleSet? exclusionRules = null)
        => UpsertPath(delta, rootId, root, path, includeChildren: Directory.Exists(path), exclusionRules, parentAncestors: null, depth: 0);

    public static bool ApplyDeleted(DeltaOverlay delta, string path)
    {
        var filePath = PathHelpers.NormalizePath(path, isDirectory: false);
        var fileId = (UInt128)PathHelpers.HashPath64(filePath);
        var removedFile = delta.Exists(fileId);
        delta.Remove(fileId);

        var directoryPath = PathHelpers.NormalizePath(path, isDirectory: true);
        var removedDir = false;
        if (!directoryPath.Equals(filePath, StringComparison.OrdinalIgnoreCase))
        {
            var dirId = (UInt128)PathHelpers.HashPath64(directoryPath);
            removedDir = delta.Exists(dirId);
            delta.Remove(dirId);
        }
        return removedFile || removedDir;
    }

    public static bool ApplyRenamed(DeltaOverlay delta, UInt128 rootId, string root, string oldPath, string newPath, ExclusionRuleSet? exclusionRules = null)
    {
        var changed = ApplyDeleted(delta, oldPath);
        changed |= UpsertPath(delta, rootId, root, newPath, includeChildren: Directory.Exists(newPath), exclusionRules, parentAncestors: null, depth: 0);
        return changed;
    }

    private static bool UpsertPath(
        DeltaOverlay delta,
        UInt128 rootId,
        string root,
        string path,
        bool includeChildren,
        ExclusionRuleSet? exclusionRules,
        AncestorNode? parentAncestors,
        int depth)
    {
        if (depth > 128)
            return false;

        FileInfo info;
        FileAttributes attributes;
        try
        {
            info = new FileInfo(path);
            attributes = info.Attributes;
        }
        catch
        {
            return false;
        }

        if ((attributes & FileAttributes.ReparsePoint) != 0)
            return false;

        var isDirectory = (attributes & FileAttributes.Directory) != 0;
        if (exclusionRules?.IsExcludedPath(path, isDirectory) == true)
            return ApplyDeleted(delta, path);

        var normalized = PathHelpers.NormalizePath(path, isDirectory);
        var normalizedRoot = PathHelpers.NormalizePath(root, isDirectory: true);
        if (normalized.Equals(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            return false;

        AncestorNode? nextAncestors = null;
        if (isDirectory)
        {
            if (parentAncestors != null && parentAncestors.Contains(normalized))
                return false;

            try
            {
                if (Directory.ResolveLinkTarget(path, returnFinalTarget: true) is { } target)
                {
                    var targetPath = PathHelpers.NormalizePath(target.FullName, isDirectory: true);
                    if (parentAncestors != null && parentAncestors.Contains(targetPath))
                        return false;
                }
            }
            catch
            {
            }

            nextAncestors = new AncestorNode(normalized, parentAncestors);
            if (nextAncestors.HasSegmentCycle())
                return false;
        }

        EnsureParentChain(delta, rootId, normalizedRoot, normalized);

        var name = Path.GetFileName(normalized.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(name))
            return false;

        var parentPath = Path.GetDirectoryName(normalized.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var parentId = string.IsNullOrWhiteSpace(parentPath) || PathHelpers.NormalizePath(parentPath, true).Equals(normalizedRoot, StringComparison.OrdinalIgnoreCase)
            ? rootId
            : (UInt128)PathHelpers.HashPath64(PathHelpers.NormalizePath(parentPath, true));

        var size = isDirectory ? 0 : info.Length;
        delta.Upsert(
            (UInt128)PathHelpers.HashPath64(normalized),
            parentId,
            name,
            FileRecordFlagsHelper.FromAttributes(attributes),
            size,
            FileTimeHelper.ToUnixSeconds(info.CreationTimeUtc),
            FileTimeHelper.ToUnixSeconds(info.LastWriteTimeUtc),
            FileTimeHelper.ToUnixSeconds(info.LastAccessTimeUtc));

        if (includeChildren && isDirectory && nextAncestors != null)
            UpsertDirectoryChildren(delta, rootId, root, normalized, exclusionRules, nextAncestors, depth + 1);

        return true;
    }

    private static void UpsertDirectoryChildren(
        DeltaOverlay delta,
        UInt128 rootId,
        string root,
        string directory,
        ExclusionRuleSet? exclusionRules,
        AncestorNode ancestors,
        int depth)
    {
        IEnumerable<string> children;
        try
        {
            children = Directory.EnumerateFileSystemEntries(directory);
        }
        catch
        {
            return;
        }

        foreach (var child in children)
            UpsertPath(delta, rootId, root, child, includeChildren: true, exclusionRules, ancestors, depth);
    }

    private static void EnsureParentChain(DeltaOverlay delta, UInt128 rootId, string normalizedRoot, string normalizedPath)
    {
        var parentPath = Path.GetDirectoryName(normalizedPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(parentPath))
            return;

        var normalizedParent = PathHelpers.NormalizePath(parentPath, isDirectory: true);
        if (normalizedParent.Equals(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            return;

        var parentId = (UInt128)PathHelpers.HashPath64(normalizedParent);
        if (delta.Exists(parentId))
            return;

        EnsureParentChain(delta, rootId, normalizedRoot, normalizedParent);

        var parentParentPath = Path.GetDirectoryName(normalizedParent.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var parentParentId = string.IsNullOrWhiteSpace(parentParentPath) || PathHelpers.NormalizePath(parentParentPath, true).Equals(normalizedRoot, StringComparison.OrdinalIgnoreCase)
            ? rootId
            : (UInt128)PathHelpers.HashPath64(PathHelpers.NormalizePath(parentParentPath, true));

        var parentName = Path.GetFileName(normalizedParent.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(parentName))
            return;

        delta.Upsert(parentId, parentParentId, parentName, FileRecordFlags.Directory, 0, 0, 0, 0);
    }
}
