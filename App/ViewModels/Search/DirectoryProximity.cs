using System.IO;

namespace Lertaro.App.ViewModels.Search;

// Where one result sits relative to the inline window's own folder. This is the primary ranking tier for
// inline results -- files directly in the folder first, then files in subfolders by how deep they are,
// then anything outside the folder -- with the shared match weight only breaking ties inside a level.
//
// Split into its own class (composition, not a partial class) rather than kept as a private helper on
// ExplorerSearchHelper: the same notion is needed both when ranking a whole snapshot and when testing a
// single path, and the sibling-file-prefix edge case below deserves its own coverage.
internal static class DirectoryProximity
{
    // Rank of a path that is not under the folder at all. A real depth can never reach this, so every
    // descendant sorts ahead of it.
    public const int Outside = int.MaxValue;

    // Normalizes a directory path for comparison: trailing separators trimmed, so "C:\Root\" and
    // "C:\Root" describe the same folder.
    public static string Normalize(string path) => path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    // 0 for a direct child, N for a descendant N levels below the folder, and Outside for a path that is
    // not under it.
    public static int Tier(string path, string contextDirectory)
    {
        var normalizedDirectory = Normalize(contextDirectory);
        var normalizedPath = Normalize(path);
        if (normalizedPath.Length <= normalizedDirectory.Length)
            return Outside;

        if (!normalizedPath.StartsWith(normalizedDirectory, StringComparison.OrdinalIgnoreCase))
            return Outside;

        // The character right after the folder must be a separator: "C:\Root" is not an ancestor of
        // "C:\Roots\file.txt" -- the paths share a prefix, but the folder boundary is what matters.
        var next = normalizedPath[normalizedDirectory.Length];
        if (next != Path.DirectorySeparatorChar && next != Path.AltDirectorySeparatorChar)
            return Outside;

        var depth = 0;
        for (var i = normalizedDirectory.Length + 1; i < normalizedPath.Length; i++)
        {
            if (normalizedPath[i] == Path.DirectorySeparatorChar || normalizedPath[i] == Path.AltDirectorySeparatorChar)
                depth++;
        }
        return depth;
    }
}
