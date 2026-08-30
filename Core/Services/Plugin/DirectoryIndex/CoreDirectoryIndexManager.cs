using System.Runtime.CompilerServices;
using System.Threading.Channels;

using Lertaro.Core.Services.Search;
namespace Lertaro.Core.Services.Plugin.DirectoryIndex;

/// <summary>
/// Managed core indexer coordinator. Decides if a path should be query-routed
/// to the USN Service via NamedPipe or scanned locally (for network/removable drives).
/// </summary>
public sealed class CoreDirectoryIndexManager
{
    private static readonly Lazy<CoreDirectoryIndexManager> _instance = new(() => new CoreDirectoryIndexManager());
    public static CoreDirectoryIndexManager Instance => _instance.Value;

    private readonly PluginDirectoryWatchRegistry _registry = new();
    private readonly PluginDirectorySearcher _searcher = new();

    private CoreDirectoryIndexManager()
    {
        // Bind the SDK delegates to this manager
        PluginSdk.Services.DirectoryIndexerService.RegisterDirectoryAction = RegisterDirectory;
        PluginSdk.Services.DirectoryIndexerService.UnregisterDirectoriesAction = UnregisterDirectories;
    }

    public void RegisterDirectory(string pluginId, string directoryPath, bool recursive, string filterPattern)
        => _registry.RegisterDirectory(pluginId, directoryPath, recursive, filterPattern);

    public void UnregisterDirectories(string pluginId) => _registry.UnregisterDirectories(pluginId);

    /// <summary>
    /// Searches files within all directories registered by the given plugin.
    /// Uses USN Service for local directories and live directory scans (exempt from exclusion rules if search query matches)
    /// for network drives/unc folders.
    /// </summary>
    public async Task<List<SearchResult>> SearchPluginDirectoriesAsync(string pluginId, string query, CancellationToken token)
    {
        var dirs = _registry.GetDirectories(pluginId);
        if (dirs == null)
            return new List<SearchResult>();

        return await _searcher.SearchAsync(dirs, query, token).ConfigureAwait(false);
    }

    /// <summary>
    /// Lists one directory (any directory, registered or not) out of the index, streaming entries as
    /// they arrive. See <see cref="IndexedDirectoryEnumerator"/> for the index-vs-live-walk routing;
    /// this only adapts its callback into the async sequence the SDK hands to plugins.
    /// </summary>
    public async IAsyncEnumerable<SearchResult> EnumerateDirectoryAsync(string directoryPath, bool recursive, string filterPattern, int limit,
        [EnumeratorCancellation] CancellationToken token = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
        var channel = Channel.CreateBounded<SearchResult>(new BoundedChannelOptions(1024)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait
        });
        var producer = Task.Run(async () =>
        {
            try
            {
                await IndexedDirectoryEnumerator.EnumerateAsync(directoryPath, recursive, filterPattern,
                    result => channel.Writer.TryWrite(result), limit, cts.Token).ConfigureAwait(false);
                channel.Writer.TryComplete();
            }
            catch (OperationCanceledException)
            {
                channel.Writer.TryComplete();
            }
            catch (Exception ex)
            {
                channel.Writer.TryComplete(ex);
            }
        }, cts.Token);

        try
        {
            await foreach (var result in channel.Reader.ReadAllAsync(token).ConfigureAwait(false))
                yield return result;
        }
        finally
        {
            // If the consumer stops early, cancel the producer so it does not keep filling the channel.
            cts.Cancel();
            await producer.ConfigureAwait(false);
        }
    }
}
