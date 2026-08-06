using Lertaro.Core.Indexer.NetworkDrive.Walk;
namespace Lertaro.Core.Indexer.NetworkDrive;

// The network/WSL counterpart to SearchEngineRecentFilesExtensions.GetRecentFiles -- UsnIndexer only
// tracks local drive letters, so a configured "Recent Files" directory under a mapped network drive or
// a WSL distro was silently skipped everywhere until this existed. Directories that don't resolve to any
// currently-CACHED network/WSL share are skipped here too (they belong to the local path instead, or
// simply haven't been indexed yet), so callers can pass the same full directory list to both sides.
public static class NetworkIndexerRecentFilesExtensions
{
    public static List<SearchResult> GetRecentFiles(this NetworkIndexer indexer, IReadOnlyList<string> directories, int limit, int maxAgeMinutes)
    {
        indexer.EnsureConfigured();

        var cutoffUtc = maxAgeMinutes > 0
            ? (uint)Math.Max(0, DateTimeOffset.UtcNow.AddMinutes(-maxAgeMinutes).ToUnixTimeSeconds())
            : 0u;

        var candidates = new List<SearchResult>();
        foreach (var dir in directories)
        {
            if (string.IsNullOrWhiteSpace(dir))
                continue;

            // Only a directory whose share already has a cache built (loaded from disk or checkpointed by
            // an in-progress build) is queried -- one that's merely configured but never indexed yet has
            // no entry in _indexes at all, so TryResolveIndex naturally skips it rather than returning an
            // empty/default index.
            if (!TryResolveIndex(indexer, dir, out var index, out var canonicalDirLower) || index == null)
                continue;

            index.CollectRecentFiles(canonicalDirLower, cutoffUtc, candidates);
        }

        var ordered = candidates.OrderByDescending(c => c.Metadata.Modified);
        return (limit > 0 ? ordered.Take(limit) : ordered).ToList();
    }

    // Windows exposes a WSL distro under more than one UNC alias depending on OS version/context --
    // "\\wsl$\{distro}" (legacy) and "\\wsl.localhost\{distro}" (current Explorer/folder-picker default)
    // both point at the exact same distro that NetworkIndexer itself always keys and stores paths under as
    // "\\wsl$\{distro}" (see NetworkIndexer.Configure and NetworkDriveSettingsHelper.GetWslDistros). A
    // "Recent Files" directory pasted or browsed to in the newer alias must still resolve to that same
    // cached index, and the path handed to RuntimeIndex must be rewritten to the alias the index's own
    // records actually use, or the subtree lookup silently finds nothing. Matching (and rewriting) by the
    // distro NAME rather than a raw string prefix also avoids "Ubuntu-22" wrongly prefix-matching "Ubuntu".
    private static bool TryResolveIndex(NetworkIndexer indexer, string dir, out NetworkIndex? index, out string canonicalDirLower)
    {
        var distro = ExtractWslDistro(dir, out var remainder);
        if (distro != null)
        {
            lock (indexer.Gate)
                index = indexer._indexes.Values.FirstOrDefault(i => string.Equals(ExtractWslDistro(i.Drive, out _), distro, StringComparison.OrdinalIgnoreCase));
            canonicalDirLower = index != null ? (index.Drive + remainder).ToLowerInvariant() : string.Empty;
            return index != null;
        }

        // Folder-index entries (a local subfolder or a UNC share/subfolder) are keyed by their own full,
        // uncollapsed path rather than a drive letter -- Path.GetPathRoot returns a UNC root like
        // "\\server\share\" (root[0] is '\\', never a letter), which the drive-letter lookup below can
        // never resolve, and even a letter-rooted local folder-index target ("C:\Users\Foo\Projects")
        // isn't keyed by its bare drive letter either. Find the longest configured key `dir` actually
        // falls under first, matching whole path segments only so "C:\Users\Foo" can't wrongly match
        // "C:\Users\FooBar". Falls back to the bare-drive-letter lookup below for a whole mapped network
        // drive, which is what NormalizeDrive collapses a real drive identity down to.
        var trimmedDir = dir.TrimEnd('\\', '/');
        NetworkIndex? bestMatch = null;
        var bestKeyLength = -1;
        lock (indexer.Gate)
        {
            foreach (var kvp in indexer._indexes)
            {
                var key = kvp.Key;
                if (key.Length <= 2)
                    continue;
                var trimmedKey = key.TrimEnd('\\', '/');
                if (!trimmedDir.StartsWith(trimmedKey, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (trimmedDir.Length > trimmedKey.Length
                    && trimmedDir[trimmedKey.Length] != '\\' && trimmedDir[trimmedKey.Length] != '/')
                    continue;
                if (trimmedKey.Length > bestKeyLength)
                {
                    bestKeyLength = trimmedKey.Length;
                    bestMatch = kvp.Value;
                }
            }
        }
        if (bestMatch != null)
        {
            index = bestMatch;
            canonicalDirLower = trimmedDir.ToLowerInvariant();
            return true;
        }

        var root = Path.GetPathRoot(dir);
        if (string.IsNullOrEmpty(root) || !char.IsLetter(root[0]))
        {
            index = null;
            canonicalDirLower = string.Empty;
            return false;
        }

        var drive = root[0].ToString().ToUpperInvariant();
        canonicalDirLower = dir.ToLowerInvariant();
        lock (indexer.Gate)
            return indexer._indexes.TryGetValue(drive, out index);
    }

    private static string? ExtractWslDistro(string path, out string remainder)
    {
        foreach (var prefix in PathHelpers.WslUncPrefixes)
        {
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                var rest = path.Substring(prefix.Length);
                var sepIndex = rest.IndexOfAny(new[] { '\\', '/' });
                remainder = sepIndex < 0 ? string.Empty : rest.Substring(sepIndex);
                return sepIndex < 0 ? rest : rest.Substring(0, sepIndex);
            }
        }
        remainder = string.Empty;
        return null;
    }
}
