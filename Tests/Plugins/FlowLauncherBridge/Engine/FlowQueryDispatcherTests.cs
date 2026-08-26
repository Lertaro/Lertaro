using Flow.Launcher.Plugin;
using Lertaro.Plugins.FlowLauncherBridge.Engine;

namespace Lertaro.Plugins.FlowLauncherBridge.Tests.Engine;

[TestClass]
public sealed class FlowQueryDispatcherTests
{
    private sealed class FakeFlowPlugin : IAsyncPlugin
    {
        public List<Result> ResultsToReturn { get; set; } = [];
        public Query? LastReceivedQuery { get; private set; }

        public Task InitAsync(PluginInitContext context) => Task.CompletedTask;

        public Task<List<Result>> QueryAsync(Query query, CancellationToken token)
        {
            LastReceivedQuery = query;
            return Task.FromResult(ResultsToReturn);
        }
    }

    [TestMethod]
    public void ParseQuery_WithEmptyInput_ReturnsEmptyQuery()
    {
        var storage = new FlowSettingsStorage(Path.GetTempPath());
        var host = new FlowPluginHost(storage, []);
        var dispatcher = new FlowQueryDispatcher(host);

        var query = dispatcher.ParseQuery("   ");

        Assert.AreEqual(string.Empty, query.Search);
        Assert.AreEqual(string.Empty, query.ActionKeyword);
    }

    [TestMethod]
    public void ParseQuery_WithoutMatchingKeyword_ReturnsGlobalSearch()
    {
        var storage = new FlowSettingsStorage(Path.GetTempPath());
        var host = new FlowPluginHost(storage, []);
        var dispatcher = new FlowQueryDispatcher(host);

        var query = dispatcher.ParseQuery("hello world");

        Assert.AreEqual("hello world", query.Search);
        Assert.AreEqual(string.Empty, query.ActionKeyword);
        Assert.HasCount(2, query.SearchTerms);
        Assert.AreEqual("hello", query.FirstSearch);
        Assert.AreEqual("world", query.SecondToEndSearch);
    }

    [TestMethod]
    public async Task DispatchQueryAsync_RoutesToGlobalPlugin()
    {
        var storage = new FlowSettingsStorage(Path.GetTempPath());
        var host = new FlowPluginHost(storage, []);
        var fakePlugin = new FakeFlowPlugin
        {
            ResultsToReturn =
            [
                new Result { Title = "Result 1", Score = 100 },
                new Result { Title = "Result 2", Score = 50 }
            ]
        };

        var metadata = new PluginMetadata
        {
            ID = "test-plugin",
            Name = "TestPlugin",
            ActionKeyword = "*"
        };

        var pair = new PluginPair { Metadata = metadata, Plugin = fakePlugin };
        host.RegisterPlugin(pair);

        var dispatcher = new FlowQueryDispatcher(host);
        var results = await dispatcher.DispatchQueryAsync("calc 1+1");

        Assert.HasCount(2, results);
        Assert.AreEqual("Result 1", results[0].Title);
        Assert.AreEqual("Result 2", results[1].Title);
    }

    [TestMethod]
    public async Task DispatchQueryAsync_DoesNotRouteDisabledKeywordPlugin()
    {
        var storage = new FlowSettingsStorage(Path.GetTempPath());
        var host = new FlowPluginHost(storage, []);
        var fakePlugin = new FakeFlowPlugin
        {
            ResultsToReturn = [new Result { Title = "Should not appear", Score = 100 }]
        };
        var pair = new PluginPair
        {
            Metadata = new PluginMetadata
            {
                ID = "disabled-plugin",
                Name = "DisabledPlugin",
                ActionKeyword = "gc",
                Disabled = true
            },
            Plugin = fakePlugin
        };
        host.RegisterPlugin(pair);

        var dispatcher = new FlowQueryDispatcher(host);
        var results = await dispatcher.DispatchQueryAsync("gc 1");

        Assert.IsEmpty(results);
        Assert.IsNull(fakePlugin.LastReceivedQuery);
    }
}
