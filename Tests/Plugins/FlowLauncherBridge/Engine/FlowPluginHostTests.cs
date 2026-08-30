using Flow.Launcher.Plugin;
using Lertaro.PluginSdk.Services;
using Lertaro.Plugins.FlowLauncherBridge.Engine;

namespace Lertaro.Plugins.FlowLauncherBridge.Tests.Engine;

[TestClass]
[DoNotParallelize]
public sealed class FlowPluginHostTests
{
    [TestInitialize]
    public void SetUp() => SettingsSearchService.InvalidateFunc = null;

    [TestCleanup]
    public void TearDown() => SettingsSearchService.InvalidateFunc = null;

    [TestMethod]
    public void RegisterPlugin_InvalidatesSettingsSearchEntries()
    {
        var invalidated = false;
        SettingsSearchService.InvalidateFunc = () => invalidated = true;
        var host = new FlowPluginHost(new FlowSettingsStorage(Path.GetTempPath()), []);

        host.RegisterPlugin(new PluginPair { Metadata = new PluginMetadata { ID = "settings-id" } });

        Assert.IsTrue(invalidated);
    }

    [TestMethod]
    public void FlowPluginHost_TracksLoadedPluginsAndKeywords()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"flow_host_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var storage = new FlowSettingsStorage(tempDir);
            var host = new FlowPluginHost(storage, [tempDir]);

            var metadata = new PluginMetadata
            {
                ID = "TEST_PLUGIN_ID",
                Name = "TestPlugin",
                ActionKeyword = "tp",
                ActionKeywords = ["tp", "testp"]
            };

            var pair = new PluginPair { Metadata = metadata };
            host.AddActionKeyword(metadata.ID, "tp");
            host.AddActionKeyword(metadata.ID, "testp");

            Assert.IsNotNull(host.KeywordPlugins);
            Assert.IsNotNull(host.GetAllPlugins());
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [TestMethod]
    public void FlowPluginHost_UpdatePluginActionKeyword_ReplacesKeywordMapping()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"flow_host_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var storage = new FlowSettingsStorage(tempDir);
            var host = new FlowPluginHost(storage, [tempDir]);

            var metadata = new PluginMetadata
            {
                ID = "TEST_PLUGIN_ID",
                Name = "MultiTranslate",
                ActionKeyword = "tr",
                ActionKeywords = ["tr"]
            };

            var pair = new PluginPair { Metadata = metadata };
            host.RegisterPlugin(pair);

            Assert.IsTrue(host.ActionKeywordAssigned("tr"));
            Assert.IsFalse(host.ActionKeywordAssigned("tra"));

            host.UpdatePluginActionKeyword("MultiTranslate", "tra");

            Assert.IsFalse(host.ActionKeywordAssigned("tr"));
            Assert.IsTrue(host.ActionKeywordAssigned("tra"));
            Assert.AreEqual("tra", pair.Metadata.ActionKeyword);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }
}
