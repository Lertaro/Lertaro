namespace Lertaro.Core.Indexer.NetworkDrive.Walk;

/// <summary>
/// An immutable, thread-safe, single-linked stack representing the chain of directory paths
/// traversed along a single branch of the filesystem tree. Used by TreeBuilder to detect and
/// prevent infinite recursion caused by symlink/junction loops on UNC network shares or WSL.
/// </summary>
internal sealed class AncestorNode
{
    public string NormalizedPath { get; }
    public AncestorNode? Parent { get; }

    /// <summary>
    /// Whether this node's own trailing segment arrived through a reparse point. The network walk descends
    /// into links, so this is what separates a symlink loop from a directory somebody named after its
    /// parent -- see <see cref="HasSegmentCycle"/>.
    /// </summary>
    public bool IsReparsePoint { get; }

    public AncestorNode(string path, AncestorNode? parent, bool isReparsePoint = false)
    {
        NormalizedPath = Normalize(path);
        Parent = parent;
        IsReparsePoint = isReparsePoint;
    }

    public bool Contains(string path)
    {
        var target = Normalize(path);
        var current = this;
        while (current != null)
        {
            if (string.Equals(current.NormalizedPath, target, StringComparison.OrdinalIgnoreCase))
                return true;
            current = current.Parent;
        }
        return false;
    }

    /// <summary>
    /// Detects if the current path chain contains repeating directory segment cycles
    /// (e.g. server-side Samba symlink loops expanding into "folderA/symlinkA/symlinkA").
    /// </summary>
    public bool HasSegmentCycle()
    {
        // A repeated name is not a loop. Without this guard, \\nas\data\backup\backup and ...\src\src --
        // directories people really do create -- were classified as cycles and skipped whole, with only an
        // aggregate counter to show for it. A repeat only means something when it arrived through a link,
        // which is the Samba shape this was written for; the exact-path Contains check, the resolved-target
        // check and the per-id enqueue guard are what stop a real cycle.
        if (!IsReparsePoint)
            return false;

        var segments = NormalizedPath.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 4)
            return false;

        // Check for 2 consecutive identical trailing segments (e.g. ".../symlinkA/symlinkA")
        if (string.Equals(segments[^1], segments[^2], StringComparison.OrdinalIgnoreCase))
            return true;

        // Check for a 2-segment repeating pattern (e.g. ".../subA/subB/subA/subB")
        if (segments.Length >= 6 &&
            string.Equals(segments[^1], segments[^3], StringComparison.OrdinalIgnoreCase) &&
            string.Equals(segments[^2], segments[^4], StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static string Normalize(string path) =>
        path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
}
