namespace Lertaro.Core.Services.Network;

/// <summary>
/// The other way to spell the same location. Index lookup is a plain prefix match against each source's
/// configured root, so one physical directory reachable under two names -- a share as either a mapped
/// letter or its UNC path, a WSL distro as either <c>\\wsl$\</c> or <c>\\wsl.localhost\</c> -- only
/// matches the spelling it happens to have been configured under. The other one silently misses every
/// index and walks the network instead, which looks like nothing worse than "slow".
/// <para>
/// This produces the alternate spelling to retry with. Deliberately just a rewrite, with no attempt to
/// decide which form is "canonical": either can be the configured one (network drives are keyed by
/// letter, WSL distros by <c>\\wsl$\</c>, but a folder index is keyed by whatever path the user typed).
/// </para>
/// </summary>
public static class IndexedPathSpelling
{
    private const string WslUnc = @"\\wsl$\";
    private const string WslLocalhost = @"\\wsl.localhost\";

    /// <summary>
    /// The spellings to try against the indexes, in order, starting with the path as given. Only worth
    /// calling for a UNC or mapped-network path: a local path has no second spelling, and finding out
    /// would cost a sweep of the session's drive mappings.
    /// </summary>
    public static IReadOnlyList<string> IndexSpellings(string path)
    {
        var alternate = AlternateSpelling(path, CurrentMappings);
        return alternate == null ? new[] { path } : new[] { path, alternate };
    }

    // Pure core, with the drive mappings passed in: (UNC target, drive letter), no trailing separator on
    // the UNC. The lookup is deferred because most calls return before ever needing it.
    internal static string? AlternateSpelling(string path, Func<IReadOnlyList<(string Unc, string Letter)>> mappings)
    {
        if (string.IsNullOrEmpty(path))
            return null;

        // Two names for the same distro share, one an alias of the other -- no mapping table involved.
        if (path.StartsWith(WslLocalhost, StringComparison.OrdinalIgnoreCase))
            return WslUnc + path.Substring(WslLocalhost.Length);
        if (path.StartsWith(WslUnc, StringComparison.OrdinalIgnoreCase))
            return WslLocalhost + path.Substring(WslUnc.Length);

        if (path.StartsWith(@"\\", StringComparison.Ordinal))
        {
            foreach (var (unc, letter) in mappings())
            {
                if (!CoversPath(unc, path))
                    continue;
                var remainder = path.Substring(unc.Length);
                return remainder.Length == 0 ? letter + @":\" : letter + ":" + remainder;
            }
            return null;
        }

        if (path.Length >= 2 && path[1] == Path.VolumeSeparatorChar)
        {
            var letter = path.Substring(0, 1);
            foreach (var (unc, mapped) in mappings())
            {
                if (mapped.Equals(letter, StringComparison.OrdinalIgnoreCase))
                    return unc + path.Substring(2);
            }
        }
        return null;
    }

    // Prefix match on whole segments, so a share does not claim a sibling that merely starts with its
    // name (\\server\share must not cover \\server\share2).
    private static bool CoversPath(string root, string path)
    {
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            return false;
        return path.Length == root.Length || path[root.Length] == Path.DirectorySeparatorChar;
    }

    // WNetGetConnection reads the session's own mapping table (no network round trip); DriveInfo.IsReady
    // is deliberately not consulted, since it can block for seconds on a disconnected share.
    private static IReadOnlyList<(string Unc, string Letter)> CurrentMappings()
    {
        var mappings = new List<(string Unc, string Letter)>();
        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (drive.DriveType != DriveType.Network)
                    continue;
                var letter = drive.Name.Substring(0, 1);
                var unc = NetworkDriveResolver.GetUncPath(letter).TrimEnd(Path.DirectorySeparatorChar);
                if (unc.Length > 0)
                    mappings.Add((unc, letter));
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"[IndexedPathSpelling] Could not read the session's drive mappings: {ex.Message}", LogLevel.Warn);
        }
        return mappings;
    }
}
