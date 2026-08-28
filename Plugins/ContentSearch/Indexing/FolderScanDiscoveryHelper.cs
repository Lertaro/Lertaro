using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.ContentSearch.Indexing;

/// <summary>
/// Scans monitored directories using SDK host index or safe filesystem enumeration to discover modified files.
/// Split out purely to keep ContentIndexScheduler under the repository per-file line limit.
/// </summary>
public static class FolderScanDiscoveryHelper
{
    public static async Task<HashSet<string>> DiscoverFilesAsync(
        ContentIndexConfig config,
        Dictionary<string, (long LastModified, long FileSize)> existingMeta,
        Action<string> onEnqueue,
        CancellationToken ct)
    {
        var discovered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pattern = config.FilterPattern;

        foreach (var rawFolder in config.MonitoredFolders)
        {
            if (ct.IsCancellationRequested) return discovered;
            var folder = ContentIndexScheduler.NormalizeFolderPath(rawFolder);
            if (string.IsNullOrEmpty(folder))
                continue;

            try
            {
                var enumeratedAny = false;
                await foreach (var item in DirectoryIndexerService.EnumerateDirectoryAsync(
                    folder,
                    recursive: true,
                    filterPattern: pattern,
                    limit: 0,
                    token: ct).ConfigureAwait(false))
                {
                    enumeratedAny = true;
                    if (ct.IsCancellationRequested) return discovered;
                    if (item.IsDir) continue;

                    var file = item.FullPath;
                    discovered.Add(file);

                    var ext = Path.GetExtension(file);
                    if (string.IsNullOrEmpty(ext) || !config.AllowedExtensions.Contains(ext))
                        continue;

                    var fileSize = item.Metadata.Size;
                    var modified = item.Metadata.Modified;
                    if (fileSize == 0 || (config.MaxFileSizeBytes > 0 && fileSize > config.MaxFileSizeBytes))
                        continue;

                    var lastModUnix = new DateTimeOffset(modified).ToUnixTimeSeconds();
                    if (existingMeta.TryGetValue(file, out var meta) &&
                        meta.LastModified == lastModUnix &&
                        meta.FileSize == fileSize)
                    {
                        continue;
                    }

                    onEnqueue(file);
                }

                if (!enumeratedAny && Directory.Exists(folder))
                {
                    ScanFilesystemFallback(folder, config, existingMeta, discovered, onEnqueue, ct);
                }
            }
            catch (OperationCanceledException) { return discovered; }
            catch
            {
                ScanFilesystemFallback(folder, config, existingMeta, discovered, onEnqueue, ct);
            }
        }

        return discovered;
    }

    private static void ScanFilesystemFallback(
        string folder,
        ContentIndexConfig config,
        Dictionary<string, (long LastModified, long FileSize)> existingMeta,
        HashSet<string> discovered,
        Action<string> onEnqueue,
        CancellationToken ct)
    {
        if (!Directory.Exists(folder)) return;
        var dirQueue = new Queue<string>();
        dirQueue.Enqueue(folder);

        while (dirQueue.Count > 0)
        {
            if (ct.IsCancellationRequested) return;
            var currentDir = dirQueue.Dequeue();

            try
            {
                foreach (var file in Directory.EnumerateFiles(currentDir, "*.*", SearchOption.TopDirectoryOnly))
                {
                    if (ct.IsCancellationRequested) return;
                    var ext = Path.GetExtension(file);
                    if (string.IsNullOrEmpty(ext) || !config.AllowedExtensions.Contains(ext))
                        continue;

                    discovered.Add(file);
                    try
                    {
                        var info = new FileInfo(file);
                        var lastWriteUnix = new DateTimeOffset(info.LastWriteTimeUtc).ToUnixTimeSeconds();
                        if (!existingMeta.TryGetValue(file, out var meta) ||
                            meta.LastModified != lastWriteUnix ||
                            meta.FileSize != info.Length)
                        {
                            onEnqueue(file);
                        }
                    }
                    catch { }
                }

                foreach (var subDir in Directory.EnumerateDirectories(currentDir, "*", SearchOption.TopDirectoryOnly))
                {
                    dirQueue.Enqueue(subDir);
                }
            }
            catch { }
        }
    }
}
