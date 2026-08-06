using System.Security.Cryptography;
using System.Text;

namespace Lertaro.Core.Indexer.NetworkDrive;

internal static class IndexerHelper
{
    // Order-independent, case-insensitive fingerprint of the global exclusion settings -- lets a resumed
    // walk tell whether rules changed since a previous store was produced (see FileRecordStore.
    // ExclusionRulesFingerprint / TreeBuilder's recheckExclusions) without any external "rules changed"
    // signal. Each category is sorted+deduped on its own so reordering entries (no membership change)
    // never spuriously flags a recheck; the category tags keep "excluded path X" from colliding with
    // "glob X"/"regex X" if the same literal string appears in more than one list.
    public static string ComputeExclusionFingerprint(
        IEnumerable<string> excludedPaths, IEnumerable<string> ignoredPathGlobs, IEnumerable<string> ignoredPathRegexes)
    {
        var sb = new StringBuilder();
        AppendCategory(sb, 'P', excludedPaths);
        AppendCategory(sb, 'G', ignoredPathGlobs);
        AppendCategory(sb, 'R', ignoredPathRegexes);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()))).ToLowerInvariant();
    }

    private static void AppendCategory(StringBuilder sb, char tag, IEnumerable<string> values)
    {
        var normalized = values
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim().ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(v => v, StringComparer.Ordinal);
        foreach (var value in normalized)
        {
            sb.Append(tag).Append(':').Append(value).Append('\n');
        }
    }

    public static string? NormalizeFilter(string? directoryFilter)
    {
        if (string.IsNullOrWhiteSpace(directoryFilter))
            return null;

        var value = directoryFilter.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar).ToLowerInvariant();
        return value.EndsWith(Path.DirectorySeparatorChar) ? value : value + Path.DirectorySeparatorChar;
    }

    public static string NormalizeDrive(string drive)
    {
        if (string.IsNullOrWhiteSpace(drive))
            return string.Empty;

        drive = drive.Trim();
        if (drive.StartsWith(@"\\") || drive.StartsWith(@"//"))
        {
            var normalized = drive.Replace('/', '\\');
            return normalized.TrimEnd('\\');
        }

        // A bare drive letter ("D", "D:", "D:\") collapses to just the letter -- the whole-drive case.
        // Anything longer is a folder-index target: a real subpath, not a drive identity, so it's
        // normalized but kept in full rather than collapsed away.
        var trimmed = drive.TrimEnd('\\');
        if (trimmed.Length is > 0 and <= 2 && char.IsLetter(trimmed[0]))
            return char.ToUpperInvariant(trimmed[0]).ToString();

        return drive.Replace('/', '\\').TrimEnd('\\');
    }

    public static string NormalizeRefreshMode(string? refreshMode) => refreshMode switch
    {
        "15Minutes" => "15Minutes",
        "Hourly" => "Hourly",
        "Daily" => "Daily",
        _ => "Manual"
    };

    public static TimeSpan? GetRefreshInterval(string refreshMode) => refreshMode switch
    {
        "15Minutes" => TimeSpan.FromMinutes(15),
        "Hourly" => TimeSpan.FromHours(1),
        "Daily" => TimeSpan.FromDays(1),
        _ => null
    };

    public static string GetCachePath(string drive) => NetworkDriveCacheLocator.GetCachePath(drive);
    public static bool HasCache(string drive) => NetworkDriveCacheLocator.HasCache(drive);
    public static IReadOnlyList<string> GetCachedDrives() => NetworkDriveCacheLocator.GetCachedDrives();
    public static void DeleteCache(string drive) => NetworkDriveCacheLocator.DeleteCache(drive);

    public static bool TryLoad(string drive, out NetworkIndex index)
        => NetworkDriveCacheLocator.TryLoad(drive, out index);

    public static void Save(NetworkIndex index) => NetworkDriveCacheLocator.Save(index);
}
