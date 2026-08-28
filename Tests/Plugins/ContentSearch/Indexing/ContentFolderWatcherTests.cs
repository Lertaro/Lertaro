using Lertaro.PluginSdk.Services;
using Lertaro.Plugins.ContentSearch.Indexing;

namespace Lertaro.Plugins.ContentSearch.Tests.Indexing;

[TestClass]
[DoNotParallelize]
public sealed class ContentFolderWatcherTests
{
    private readonly List<string> _registeredPaths = new();
    private readonly List<string> _unregisteredPluginIds = new();

    [TestInitialize]
    public void SetUp()
    {
        _registeredPaths.Clear();
        _unregisteredPluginIds.Clear();

        DirectoryIndexerService.RegisterDirectoryAction = (pluginId, dir, rec, pat) => _registeredPaths.Add(dir);
        DirectoryIndexerService.UnregisterDirectoriesAction = pluginId => _unregisteredPluginIds.Add(pluginId);
    }

    [TestCleanup]
    public void TearDown()
    {
        DirectoryIndexerService.RegisterDirectoryAction = null;
        DirectoryIndexerService.UnregisterDirectoriesAction = null;
    }

    [TestMethod]
    public void UpdateFolders_RegistersDirectoriesViaDirectoryIndexerService()
    {
        var tempFolder = Path.Combine(Path.GetTempPath(), "TestCSWatcher_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempFolder);

        try
        {
            var triggered = false;
            using var watcher = new ContentFolderWatcher(() => triggered = true);

            watcher.UpdateFolders(new[] { tempFolder });

            Assert.Contains("Lertaro.Plugins.ContentSearch", _unregisteredPluginIds);
            Assert.Contains(tempFolder, _registeredPaths);

            DirectoryIndexerService.NotifyDirectoryChanged("Lertaro.Plugins.ContentSearch");
            Assert.IsTrue(triggered);
        }
        finally
        {
            if (Directory.Exists(tempFolder))
            {
                try { Directory.Delete(tempFolder, true); } catch { }
            }
        }
    }
}
