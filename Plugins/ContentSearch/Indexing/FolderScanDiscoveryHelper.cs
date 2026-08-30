using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.ContentSearch.Indexing;

/// <summary>
/// Scans monitored directories using the SDK host index plus a filesystem walk to discover modified files.
/// The host index can answer from a stale snapshot (e.g. the service has not yet picked up recently
/// created files) and a non-empty enumeration carries no completeness signal, so the filesystem walk
/// always runs as well and the two result sets merge. Split out purely to keep ContentIndexScheduler
/// under the repository per-file line limit.
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
                await foreach (var item in DirectoryIndexerService.EnumerateDirectoryAsync(
                    folder,
                    recursive: true,
                    filterPattern: pattern,
                    limit: 0,
                    token: ct).ConfigureAwait(false))
                {
                    if (ct.IsCancellationRequested) return discovered;
                    if (item.IsDir) continue;

                    var file = item.FullPath;
                    if (config.IsExcluded(file)) continue;

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
            }
            catch (OperationCanceledException) { return discovered; }
            catch
            {
                // Host enumeration unavailable (service down, pipe timeout): the filesystem
                // walk below still covers the folder, so keep going instead of failing.
            }

            // Runs even when the host enumeration answered: its snapshot may be stale.
            if (ct.IsCancellationRequested) return discovered;
            ScanFilesystem(folder, config, existingMeta, discovered, onEnqueue, ct);
        }

        return discovered;
    }

    private static void ScanFilesystem(
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
                    if (config.IsExcluded(file)) continue;

                    var ext = Path.GetExtension(file);
                    if (string.IsNullOrEmpty(ext) || !config.AllowedExtensions.Contains(ext))
                        continue;

                    // The host enumeration already reported (and possibly enqueued) this file;
                    // keep the onEnqueue contract at "at most once per file".
                    if (!discovered.Add(file))
                        continue;

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
                    // A matching directory drops its entire subtree from the walk.
                    if (!config.IsExcluded(subDir))
                    {
                        dirQueue.Enqueue(subDir);
                    }
                }
            }
            catch { }
        }
    }
}
