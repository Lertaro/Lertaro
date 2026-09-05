using Lertaro.Core.IndexV2.Persistence;
namespace Lertaro.Core.Indexer.Usn;

internal static class LocalDriveCacheLocator
{
    // Single source of truth for the local-drive index cache directory. Writers and readers of these
    // .idx files (snapshot saves, cold-start restore, rebuilds, recovery) must all agree on one
    // directory: the shared data dir, so the service and the app operate on the same cache files.
    // (A per-user path here made the debounced folder-drive snapshot writer save where no reader
    // ever looked, silently discarding increments across restarts.)
    public static readonly string DefaultCacheDir = Path.Combine(Logger.SharedDataDir, "indexes");

    public static string GetCachePath(string cacheDir, string drive) => FileRecordStoreSerializer.GetBasePath(cacheDir, GetRequiredCacheKey(drive)) + ".idx";

    public static bool HasCache(string cacheDir, string drive)
    {
        var key = GetCacheKey(drive);
        return key != null && File.Exists(FileRecordStoreSerializer.GetBasePath(cacheDir, key) + ".idx");
    }

    public static void Delete(string cacheDir, string drive)
    {
        var normalizedDrive = NormalizeDrive(drive);
        var key = GetCacheKey(drive);
        if (key != null)
        {
            var path = FileRecordStoreSerializer.GetBasePath(cacheDir, key) + ".idx";
            if (File.Exists(path))
            {
                TryDelete(path);
                if (!File.Exists(path))
                    return;
            }
        }

        // An unavailable drive cannot provide a live volume identity. Use the path found by reading
        // the snapshot header so deleting an unplugged drive still removes its cached index.
        var cachedEntry = ListCachedDrives(cacheDir).FirstOrDefault(entry =>
            entry.Drive.Equals(normalizedDrive, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrEmpty(cachedEntry.Path))
            TryDelete(cachedEntry.Path);
    }

    // Drive letter + the actual on-disk cache file path that was found for it. GetCachePath can't be
    // used to recover this path for a drive that isn't currently present: it derives the filename from
    // a live volume identity (VolumeHelper.GetVolumeIdentity), which requires the volume to be mounted
    // to query. ListCachedDrives already has to open every .idx file to read its SourceKey anyway, so it
    // carries the path it found that key at back to the caller instead of discarding it.
    public readonly record struct CachedDriveEntry(string Drive, string Path);

    // Drives with an on-disk cache but not currently detected (unplugged, disconnected) still need a
    // status row -- otherwise they'd vanish from the list entirely instead of showing as "unavailable".
    public static IReadOnlyList<CachedDriveEntry> ListCachedDrives(string cacheDir)
    {
        if (!Directory.Exists(cacheDir))
            return Array.Empty<CachedDriveEntry>();

        var entries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in Directory.EnumerateFiles(cacheDir, "*.idx"))
        {
            SnapshotFormat.Meta? meta;
            try
            {
                meta = SnapshotFormat.TryReadHeaderFromFile(path);
            }
            catch (IOException)
            {
                continue; // mid-write, not corruption -- picked up again next refresh
            }

            if (meta == null)
            {
                TryDelete(path);
                continue;
            }

            if (meta.SourceKind == FileRecordSourceKind.LocalMft)
            {
                var drive = NormalizeDrive(meta.SourceKey);
                if (drive.Length == 1)
                    entries[drive] = path;
            }
        }
        return entries
            .Select(kvp => new CachedDriveEntry(kvp.Key, kvp.Value))
            .OrderBy(e => e.Drive, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
        }
    }

    private static string GetRequiredCacheKey(string drive)
        => GetCacheKey(drive) ?? throw new InvalidOperationException($"Volume identity unavailable for drive {drive}.");

    private static string? GetCacheKey(string drive)
    {
        var identity = VolumeHelper.GetVolumeIdentity(drive);
        return identity.HasValue ? VolumeHelper.GetVolumeCacheKey(identity.Value) : null;
    }

    private static string NormalizeDrive(string drive) => string.IsNullOrWhiteSpace(drive)
        ? string.Empty
        : drive.Trim().TrimEnd(':', '\\').ToUpperInvariant();
}
