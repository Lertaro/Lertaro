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

    public AncestorNode(string path, AncestorNode? parent)
    {
        NormalizedPath = Normalize(path);
        Parent = parent;
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
