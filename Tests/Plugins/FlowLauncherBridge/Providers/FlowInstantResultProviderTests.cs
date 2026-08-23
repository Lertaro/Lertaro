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

        host.RegisterPlugin(pair);

        var dispatcher = new FlowQueryDispatcher(host);
        var provider = new FlowInstantResultProvider(dispatcher);

        var results = provider.GetInstantResults("test query").ToList();

        Assert.HasCount(1, results);
        Assert.AreEqual("Flow Result Title", results[0].Title);
        Assert.AreEqual("Flow Result SubTitle", results[0].Description);
    }

    [TestMethod]
    public void GetInstantResults_WhenTriggerKeywordTyped_ReturnsPluginList()
    {
        var storage = new FlowSettingsStorage(Path.GetTempPath());
        var host = new FlowPluginHost(storage, []);
        var pair = new PluginPair
        {
            Metadata = new PluginMetadata { ID = "yt", Name = "YouTube", ActionKeyword = "yt" },
            Plugin = new FakeFlowPlugin()
        };
        host.RegisterPlugin(pair);

        var dispatcher = new FlowQueryDispatcher(host);
        var provider = new FlowInstantResultProvider(dispatcher, host);

        var results = provider.GetInstantResults("flow").ToList();

        Assert.HasCount(1, results);
        Assert.Contains("YouTube", results[0].Title);
    }

    [TestMethod]
    public void GetInstantResults_WhenTriggerKeywordWithFilterMatchingPlugin_ReturnsFiltered()
    {
        var storage = new FlowSettingsStorage(Path.GetTempPath());
        var host = new FlowPluginHost(storage, []);
        var pair1 = new PluginPair
        {
            Metadata = new PluginMetadata { ID = "yt", Name = "YouTube", ActionKeyword = "yt" },
            Plugin = new FakeFlowPlugin()
        };
        var pair2 = new PluginPair
        {
            Metadata = new PluginMetadata { ID = "todo", Name = "QuickTodo", ActionKeyword = "todo" },
            Plugin = new FakeFlowPlugin()
        };
        host.RegisterPlugin(pair1);
        host.RegisterPlugin(pair2);

        var dispatcher = new FlowQueryDispatcher(host);
        var provider = new FlowInstantResultProvider(dispatcher, host);

        var results = provider.GetInstantResults("flow yt").ToList();

        Assert.HasCount(1, results);
        Assert.Contains("YouTube", results[0].Title);
    }

    [TestMethod]
    public void GetInstantResults_WhenTriggerKeywordWithFilterNotMatching_ReturnsEmpty()
    {
        var storage = new FlowSettingsStorage(Path.GetTempPath());
        var host = new FlowPluginHost(storage, []);
        var pair = new PluginPair
        {
            Metadata = new PluginMetadata { ID = "yt", Name = "YouTube", ActionKeyword = "yt" },
            Plugin = new FakeFlowPlugin()
        };
        host.RegisterPlugin(pair);

        var dispatcher = new FlowQueryDispatcher(host);
        var provider = new FlowInstantResultProvider(dispatcher, host);

        var results = provider.GetInstantResults("flow nomatchhere").ToList();

        Assert.IsEmpty(results);
    }

    [TestMethod]
    public void GetInstantResults_WhenTriggerKeywordWithInstall_RoutesToCommunityList()
    {
        var storage = new FlowSettingsStorage(Path.GetTempPath());
        var host = new FlowPluginHost(storage, []);
        var dispatcher = new FlowQueryDispatcher(host);
        var provider = new FlowInstantResultProvider(dispatcher, host);

        var results = provider.GetInstantResults("flow install").ToList();

        Assert.IsNotEmpty(results);
    }

    [TestMethod]
    public void GetInstantResults_WhenTriggerKeywordWithUpdate_RoutesToUpdateList()
    {
        var storage = new FlowSettingsStorage(Path.GetTempPath());
        var host = new FlowPluginHost(storage, []);
        var dispatcher = new FlowQueryDispatcher(host);
        var provider = new FlowInstantResultProvider(dispatcher, host);

        var results = provider.GetInstantResults("flow update").ToList();

        Assert.IsNotEmpty(results);
    }

    [TestMethod]
    public void GetInstantResults_WhenTriggerKeywordWithUninstall_RoutesToUninstallList()
    {
        var storage = new FlowSettingsStorage(Path.GetTempPath());
        var host = new FlowPluginHost(storage, []);
        var dispatcher = new FlowQueryDispatcher(host);
        var provider = new FlowInstantResultProvider(dispatcher, host);

        var results = provider.GetInstantResults("flow uninstall").ToList();

        Assert.IsNotEmpty(results);
    }
}
