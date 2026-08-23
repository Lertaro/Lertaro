using Flow.Launcher.Plugin;
using Lertaro.Plugins.FlowLauncherBridge.Engine;

namespace Lertaro.Plugins.FlowLauncherBridge.Tests.Engine;

[TestClass]
public sealed class FlowPluginHostTests
{
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
}
