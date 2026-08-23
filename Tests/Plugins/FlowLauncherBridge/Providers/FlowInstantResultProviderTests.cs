using Flow.Launcher.Plugin;
using Lertaro.Plugins.FlowLauncherBridge.Engine;
using Lertaro.Plugins.FlowLauncherBridge.Providers;

namespace Lertaro.Plugins.FlowLauncherBridge.Tests.Providers;

[TestClass]
public sealed class FlowInstantResultProviderTests
{
    private sealed class FakeFlowPlugin : IAsyncPlugin
    {
        public Task InitAsync(PluginInitContext context) => Task.CompletedTask;

        public Task<List<Result>> QueryAsync(Query query, CancellationToken token) => Task.FromResult(new List<Result>
            {
                new() { Title = "Flow Result Title", SubTitle = "Flow Result SubTitle" }
            });
    }

    [TestMethod]
    public void GetInstantResults_WhenQueryIsEmpty_ReturnsEmpty()
    {
        var storage = new FlowSettingsStorage(Path.GetTempPath());
        var host = new FlowPluginHost(storage, []);
        var dispatcher = new FlowQueryDispatcher(host);
        var provider = new FlowInstantResultProvider(dispatcher);

        var results = provider.GetInstantResults(string.Empty).ToList();

        Assert.IsEmpty(results);
    }

    [TestMethod]
    public void GetInstantResults_WhenPluginReturnsResults_ReturnsMappedInstantItems()
    {
        var storage = new FlowSettingsStorage(Path.GetTempPath());
        var host = new FlowPluginHost(storage, []);
        var pair = new PluginPair
        {
            Metadata = new PluginMetadata { ID = "p1", Name = "P1", ActionKeyword = "*" },
            Plugin = new FakeFlowPlugin()
        };

        typeof(FlowPluginHost)
            .GetMethod("RegisterPluginKeywords", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(host, [pair]);

        var dispatcher = new FlowQueryDispatcher(host);
        var provider = new FlowInstantResultProvider(dispatcher);

        var results = provider.GetInstantResults("test query").ToList();

        Assert.HasCount(1, results);
        Assert.AreEqual("Flow Result Title", results[0].Title);
        Assert.AreEqual("Flow Result SubTitle", results[0].Description);
    }
}
