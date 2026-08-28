using Lertaro.Plugins.ContentSearch.Indexing;

namespace Lertaro.Plugins.ContentSearch.Tests.Indexing;

[TestClass]
public sealed class FolderScanDiscoveryHelperTests
{
    [TestMethod]
    public async Task DiscoverFilesAsync_EmptyFolders_ReturnsEmpty()
    {
        var config = new ContentIndexConfig
        {
            MonitoredFolders = new List<string>()
        };
        var existingMeta = new Dictionary<string, (long, long)>();
        var enqueued = new List<string>();

        var discovered = await FolderScanDiscoveryHelper.DiscoverFilesAsync(
            config,
            existingMeta,
            enqueued.Add,
            CancellationToken.None);

        Assert.IsEmpty(discovered);
        Assert.IsEmpty(enqueued);
    }
}
