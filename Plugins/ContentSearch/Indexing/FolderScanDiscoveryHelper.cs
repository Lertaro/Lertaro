using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.ContentSearch.Indexing;

/// <summary>
/// Discovers modified files through the SDK host index. The host owns the index-vs-filesystem routing;
/// this plugin must not walk monitored directories itself.
/// Split out purely to keep ContentIndexScheduler under the repository per-file line limit.
/// </summary>
public static class FolderScanDiscoveryHelper
{
    public static async Task<HashSet<string>> DiscoverFilesAsync(
        ContentIndexConfig config,
        Dictionary<string, (long LastModified, long FileSize, int MissingCount)> existingMeta,
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
                    // ponytail: new oversized files deliberately stay unindexed. An
                    // already-indexed file that becomes oversized after the cap was
                    // lowered keeps its stale indexed text until a full index rebuild:
                    // pruning a file that still exists and is still in scope was judged
                    // worse than serving stale text (to prune those rows instead,
                    // filter by size in TriggerFullScan's toDeleteImmediately).
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
        }

        return discovered;
    }
}
