namespace Lertaro.Core.Indexer.NetworkDrive.Walk;

internal static class PathHelpers
{
    public static string NormalizePath(string path, bool isDirectory)
    {
        var normalized = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        if (isDirectory && !normalized.EndsWith(Path.DirectorySeparatorChar))
            normalized += Path.DirectorySeparatorChar;
        return normalized;
    }

    // A bare drive letter ("Z") needs ":\" appended to form a root. Anything else (a UNC path or a
    // folder-index target, e.g. "Z:\Archive") is already a full path and just needs a trailing separator --
    // blindly appending ":\" there would produce "Z:\Archive:\", a colon in the middle of the path that can
    // never resolve. The single call site for each of the three shapes this handles: RuntimeIndex.Load
    // building a runtime source root, DriveRefreshRunner rooting a scan pass, and WatcherManager/
    // DriveWatcherHost translating a raw drive key back into a root to diff a watcher event against.
    public static string BuildSourceRoot(string sourceKey) =>
        sourceKey.Length == 1 ? sourceKey + @":\"
        : sourceKey.EndsWith(Path.DirectorySeparatorChar) || sourceKey.EndsWith(Path.AltDirectorySeparatorChar) ? sourceKey
        : sourceKey + Path.DirectorySeparatorChar;

    // "\\wsl$\..."/"\\wsl.localhost\..." specifically -- NOT every "\\wsl"-prefixed path, so a real UNC
    // share whose hostname happens to start with "wsl" (e.g. "\\wslbackup\share") isn't misclassified as
    // a WSL distro. Mirrors NetworkDriveSettingsHelper.IsWslPath in the App layer (which can't reference
    // this internal Core type), kept as the single definition for Core-side callers.
    public static readonly string[] WslUncPrefixes = { @"\\wsl$\", @"\\wsl.localhost\" };
    public static bool IsWslUncPath(string path) => WslUncPrefixes.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase));

    public static UInt128 HashPath(string path)
    {
        var normalized = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
            .ToLowerInvariant();

        var low = 14695981039346656037UL;
        var high = 1099511628211UL;
        foreach (var c in normalized)
        {
            low ^= c;
            low *= 1099511628211UL;
            high ^= (uint)c + 0x9E3779B97F4A7C15UL;
            high *= 14029467366897019727UL;
        }

        return new UInt128(high, low);
    }

    public static ulong HashPath64(string path)
    {
        var hash = HashPath(path);
        var low = (ulong)hash;
        var high = (ulong)(hash >> 64);
        var value = low ^ high;
        return value == 0 ? 1 : value;
    }
}
