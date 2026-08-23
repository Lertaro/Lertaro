using Flow.Launcher.Plugin;
using Lertaro.Plugins.FlowLauncherBridge.Engine;

namespace Lertaro.Plugins.FlowLauncherBridge.Tests.Engine;

[TestClass]
public sealed class FlowPluginHostUnloadTests
{
    private sealed class FakeDisposablePlugin : IAsyncPlugin, IAsyncDisposable
    {
        public bool IsDisposed { get; private set; }
        public Task InitAsync(PluginInitContext context) => Task.CompletedTask;
        public Task<List<Result>> QueryAsync(Query query, CancellationToken token) => Task.FromResult(new List<Result>());
        public ValueTask DisposeAsync()
        {
            IsDisposed = true;
            return ValueTask.CompletedTask;
        }
    }

    [TestMethod]
    public async Task UnloadPluginAsync_RemovesAndDisposesPlugin()
    {
        var storage = new FlowSettingsStorage(Path.GetTempPath());
        var host = new FlowPluginHost(storage, []);
        var plugin = new FakeDisposablePlugin();
        var pair = new PluginPair
        {
            Metadata = new PluginMetadata { ID = "unload-id", Name = "UnloadMe", ActionKeyword = "unl" },
            Plugin = plugin
        };

        host.RegisterPlugin(pair);
        Assert.HasCount(1, host.GetAllPlugins());

        var unloaded = await host.UnloadPluginAsync("unload-id");

        Assert.IsTrue(unloaded);
        Assert.IsEmpty(host.GetAllPlugins());
        Assert.IsTrue(plugin.IsDisposed);
    }
}
