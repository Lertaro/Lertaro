using System.Security.Cryptography;
using System.Text;

using Lertaro.Core.Services.Network;

using Lertaro.Core.IndexV2.Persistence;
namespace Lertaro.Core.Indexer.NetworkDrive;

internal static class NetworkDriveCacheLocator
{
    public static string GetCachePath(string drive)
        => FileRecordStoreSerializer.GetBasePath(Path.Combine(Logger.UserDataDir, "indexes"), GetStorageKeyOrFallback(drive)) + ".idx";

    public static bool HasCache(string drive)
    {
        var key = TryResolveStorageKey(drive);
        if (key == null)
            return false;
        var cacheDir = Path.Combine(Logger.UserDataDir, "indexes");
        return File.Exists(FileRecordStoreSerializer.GetBasePath(cacheDir, key) + ".idx");
    }

    public static IReadOnlyList<string> GetCachedDrives()
    {
        var resolvedByUnc = NetworkDriveResolver.GetNetworkDrives()
            .Where(d => !string.IsNullOrWhiteSpace(d.UncPath))
            .ToDictionary(d => NormalizeUnc(d.UncPath), d => d.Letter, StringComparer.OrdinalIgnoreCase);

        // Was filtered to single-letter drives only, which silently made this never return UNC/WSL/
        // folder-index keys -- e.g. the App's own "cached but currently unchecked WSL row" logic
        // (NetworkDriveSettingsViewModel filtering this list for entries starting with "\\") was dead
        // code, always empty. Broadened to return every distinct normalized key regardless of shape.
        return EnumerateNetworkStores()
            .Select(store =>
            {
                var unc = NormalizeUnc(store.FileSystemType);
                return unc.Length > 0 && resolvedByUnc.TryGetValue(unc, out var currentDrive)
                    ? currentDrive
                    : IndexerHelper.NormalizeDrive(store.SourceKey);
            })
            .Where(drive => !string.IsNullOrEmpty(drive))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(drive => drive, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static void DeleteCache(string drive)
    {
        var storageKey = TryResolveStorageKey(drive);
        if (storageKey == null)
            return;
        var cacheDir = Path.Combine(Logger.UserDataDir, "indexes");
        TryDelete(FileRecordStoreSerializer.GetBasePath(cacheDir, storageKey) + ".idx");
    }

    public static bool TryLoad(string drive, out NetworkIndex index)
    {
        index = new NetworkIndex(drive);
        var storageKey = TryResolveStorageKey(drive);
        if (storageKey == null)
            return false;

        var cacheDir = Path.Combine(Logger.UserDataDir, "indexes");
        var path = FileRecordStoreSerializer.GetBasePath(cacheDir, storageKey) + ".idx";
        if (!File.Exists(path))
            return false; // no cache -- caller does a full fresh rebuild instead

        try
        {
            index = NetworkIndex.FromSnapshotFile(IndexerHelper.NormalizeDrive(drive), path);
            return true;
        }
        catch (Exception ex)
        {
            Logger.Log($"[NetworkDriveCacheLocator] Failed to open IndexV2 cache for {drive}: {ex.Message}", LogLevel.Error);
            return false;
        }
    }

    public static void Save(NetworkIndex index) => index.SaveToCache(GetCachePath(index.Drive));

    // Memoizes the last successfully-resolved key per drive letter. GetCachePath is called fresh for
    // EVERY write during a walk (each periodic checkpoint, plus the final save -- see
    // NetworkIndex.FromStore), and GetUncPath is a live WNetGetConnection syscall that returns empty on
    // any transient failure (disconnect, ERROR_NOT_CONNECTED, ...). Recomputing the key from scratch on
    // every call meant a connection blip mid-walk flipped the destination between SHA256(unc) and
    // SHA256(letter) -- two totally different hashes -- so different checkpoints of the SAME walk wrote
    // to DIFFERENT files, and whichever one a later write abandoned was never revisited or cleaned up
    // (see FileRecordStoreReplaceHelper). Only a SUCCESSFUL resolution updates the cache, so a real
    // reconnect-to-a-different-share still gets picked up eventually; a transient blip just reuses
    // whatever last resolved instead of computing a different fallback.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _resolvedKeyCache = new(StringComparer.OrdinalIgnoreCase);

    private static string GetStorageKeyOrFallback(string drive)
    {
        var unc = NetworkDriveResolver.GetUncPath(drive);
        if (!string.IsNullOrWhiteSpace(unc))
            return _resolvedKeyCache[drive] = BuildStorageKey(unc);

        if (_resolvedKeyCache.TryGetValue(drive, out var cached))
            return cached;

        var fallback = BuildFallbackStorageKey(IndexerHelper.NormalizeDrive(drive));
        _resolvedKeyCache[drive] = fallback;
        return fallback;
    }

    private static string? TryResolveStorageKey(string drive)
    {
        var normalizedDrive = IndexerHelper.NormalizeDrive(drive);
        if (normalizedDrive.Length == 0)
            return null;

        var unc = NetworkDriveResolver.GetUncPath(normalizedDrive);
        if (!string.IsNullOrWhiteSpace(unc))
            return BuildStorageKey(unc);

        var cacheDir = Path.Combine(Logger.UserDataDir, "indexes");

        // A folder-index target has no UNC to resolve, ever -- GetStorageKeyOrFallback saved it under
        // BuildFallbackStorageKey(drive) directly. Check that exact key before falling through to the
        // FileSystemType-based scan below, which for a folder index is empty (never a real UNC) and
        // would resolve to a filename nothing was ever saved under, silently orphaning the cache on
        // delete/reload.
        var fallbackKey = BuildFallbackStorageKey(normalizedDrive);
        if (File.Exists(FileRecordStoreSerializer.GetBasePath(cacheDir, fallbackKey) + ".idx"))
            return fallbackKey;

        // Last resort for a drive/share that WAS connected when saved (so its cache's FileSystemType holds
        // the real UNC it was keyed under) but is disconnected right now.
        var fallback = EnumerateNetworkStores()
            .FirstOrDefault(store => store.SourceKey.TrimEnd(':')
                .Equals(normalizedDrive.TrimEnd(':'), StringComparison.OrdinalIgnoreCase));
        return fallback.SourceKey == null ? null : BuildStorageKey(fallback.FileSystemType);
    }

    private readonly record struct StoreSummary(string SourceKey, string FileSystemType, FileRecordSourceKind SourceKind);

    private static IEnumerable<StoreSummary> EnumerateNetworkStores()
    {
        var cacheDir = Path.Combine(Logger.UserDataDir, "indexes");
        if (!Directory.Exists(cacheDir))
            yield break;

        foreach (var path in Directory.EnumerateFiles(cacheDir, "*.idx"))
        {
            SnapshotFormat.Meta? meta;
            try
            {
                meta = SnapshotFormat.TryReadHeaderFromFile(path);
            }
            catch (IOException)
            {
                continue; // mid-write, not corruption -- picked up again next enumeration
            }

            if (meta == null)
            {
                TryDelete(path);
                continue;
            }
            if (meta.SourceKind == FileRecordSourceKind.NetworkMappedDrive)
                yield return new StoreSummary(meta.SourceKey, meta.FileSystemType, meta.SourceKind);
        }
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

    public static string GetIdForUnc(string unc)
    {
        var normalized = NormalizeUnc(unc).ToLowerInvariant();
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string BuildStorageKey(string unc) => GetIdForUnc(unc);

    private static string BuildFallbackStorageKey(string drive)
    {
        var normalized = IndexerHelper.NormalizeDrive(drive).ToLowerInvariant();
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string NormalizeUnc(string? unc)
        => string.IsNullOrWhiteSpace(unc)
            ? string.Empty
            // Replace before trim -- a '/'-terminated path (e.g. "\\server\share/") would otherwise
            // survive TrimEnd('\\') untouched, since that only strips backslashes.
            : unc.Trim().Replace('/', '\\').TrimEnd('\\');
}
