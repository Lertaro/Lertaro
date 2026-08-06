namespace Lertaro.Core.Indexer.Usn;

internal static class DriveMaintenanceHelper
{
    public static string NormalizeDrive(string drive) => string.IsNullOrWhiteSpace(drive)
        ? string.Empty
        : drive.Trim().TrimEnd(':', '\\').ToUpperInvariant();

    public static UsnIndexer.DriveIndexStatus UpdateStatus(
        string drive,
        bool isPresent,
        bool isEnabled,
        string indexCacheDir,
        Dictionary<string, UsnIndexer.DriveIndexStatus> current,
        List<string> drivesToBuild,
        IReadOnlyDictionary<string, string> cachedPaths)
    {
        // GetCachePath derives the filename from a live volume identity query, which only works while
        // the drive is actually mounted -- a not-present drive (unplugged) has no way to re-derive that
        // identity, so its cache path (if any) has to come from cachedPaths instead, which was already
        // resolved by opening the .idx file itself (see LocalDriveCacheLocator.ListCachedDrives). Without
        // this, a disabled-and-not-present drive with a leftover cache file would report an empty
        // CachePath and the Settings UI could never offer to delete it (unlike network/WSL drives, whose
        // cache-existence check never depended on live reachability to begin with).
        var cachePath = isPresent
            ? LocalDriveCacheLocator.GetCachePath(indexCacheDir, drive)
            : cachedPaths.GetValueOrDefault(drive, string.Empty);

        if (current.TryGetValue(drive, out var existing))
        {
            var wasEnabled = existing.Enabled;
            var hasCache = LocalDriveCacheLocator.HasCache(indexCacheDir, drive);
            existing.Enabled = isPresent && isEnabled;
            existing.Kind = isPresent ? VolumeHelper.GetDisplayFileSystemType(drive) : "-";
            existing.State = isPresent ? existing.State : "unavailable";
            existing.CachePath = cachePath;
            if (!isPresent)
            {
                existing.Files = 0;
                existing.Dirs = 0;
            }
            else if (!wasEnabled && isEnabled && !hasCache && existing.State is not "indexing" and not "pending")
            {
                existing.State = "pending";
                drivesToBuild.Add(drive);
            }
            return existing;
        }

        var shouldBuild = isPresent && isEnabled && !LocalDriveCacheLocator.HasCache(indexCacheDir, drive);
        if (shouldBuild)
            drivesToBuild.Add(drive);
        return new UsnIndexer.DriveIndexStatus
        {
            Drive = drive,
            Enabled = isPresent && isEnabled,
            Kind = isPresent ? VolumeHelper.GetDisplayFileSystemType(drive) : "-",
            State = shouldBuild ? "pending" : isPresent && isEnabled ? "ready" : isPresent ? "disabled" : "unavailable",
            CachePath = cachePath
        };
    }
}
