using Flow.Launcher.Plugin;
using Lertaro.Plugins.FlowLauncherBridge.Engine;
using Lertaro.Plugins.FlowLauncherBridge.Providers;

namespace Lertaro.Plugins.FlowLauncherBridge.Tests.Providers;

[TestClass]
[DoNotParallelize]
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

    // SearchRefreshService is process-wide static state, so it is only ever touched by the tests below
    // and reset around each of them (a leftover stub delegate from one test would otherwise be invoked by
    // another test's dispatch and change its outcome).
    [TestInitialize]
    public void ResetRefreshSeam() => PluginSdk.Services.SearchRefreshService.RefreshMatchingFunc = null;

    [TestCleanup]
    public void ClearRefreshSeam() => PluginSdk.Services.SearchRefreshService.RefreshMatchingFunc = null;

    [TestMethod]
    public void GetInstantResults_WhenDispatchAnswersWithinTimeout_DoesNotRequestARefresh()
    {
        // A dispatch that answers fast hands its results straight back to the caller, so they are already
        // on screen -- asking the host to re-run the query on top of that searched the same text twice per
        // keystroke. This is the regression: it used to refresh from inside the task body unconditionally.
        var storage = new FlowSettingsStorage(Path.GetTempPath());
        var host = new FlowPluginHost(storage, []);
        host.RegisterPlugin(new PluginPair
        {
            Metadata = new PluginMetadata { ID = "p1", Name = "P1", ActionKeyword = "*" },
            Plugin = new FakeFlowPlugin()
        });

        var refreshCalls = 0;
        PluginSdk.Services.SearchRefreshService.RefreshMatchingFunc = _ => Interlocked.Increment(ref refreshCalls);

        var dispatcher = new FlowQueryDispatcher(host);
        var provider = new FlowInstantResultProvider(dispatcher);

        var results = provider.GetInstantResults("dev").ToList();

        Assert.IsNotEmpty(results, "the fast path is the one that should have returned these results");
        Thread.Sleep(200);
        Assert.AreEqual(0, refreshCalls,
            "a dispatch that already returned its results must not also ask the host to re-run the search");
    }

    [TestMethod]
    public void GetInstantResults_WhenDispatchExceedsTimeout_RequestsOneRefreshAfterItLands()
    {
        // The timeout ran out, so a "query pending" placeholder is what the user is looking at. Once the
        // dispatch finishes, the host must re-run the query so the real results replace that placeholder.
        var storage = new FlowSettingsStorage(Path.GetTempPath());
        var host = new FlowPluginHost(storage, []);
        host.RegisterPlugin(new PluginPair
        {
            Metadata = new PluginMetadata { ID = "slow", Name = "Slow", ActionKeyword = "*" },
            Plugin = new SlowFlowPlugin(TimeSpan.FromMilliseconds(700))
        });

        var refreshCalls = 0;
        var matchedQuery = string.Empty;
        PluginSdk.Services.SearchRefreshService.RefreshMatchingFunc = predicate =>
        {
            if (predicate("dev"))
            {
                Volatile.Write(ref matchedQuery, "dev");
                Interlocked.Increment(ref refreshCalls);
            }
        };

        var dispatcher = new FlowQueryDispatcher(host);
        var provider = new FlowInstantResultProvider(dispatcher);

        var results = provider.GetInstantResults("dev").ToList();

        Assert.HasCount(1, results, "the caller should get the pending placeholder, not the late results");

        // The refresh is attached as a continuation on the still-running dispatch, so it only fires once
        // that dispatch completes -- poll rather than assume a fixed delay.
        var deadline = Environment.TickCount64 + 3000;
        while (Volatile.Read(ref refreshCalls) == 0 && Environment.TickCount64 < deadline)
            Thread.Sleep(50);

        Assert.AreEqual(1, Volatile.Read(ref refreshCalls), "the late dispatch must ask for exactly one refresh");
        Assert.AreEqual("dev", Volatile.Read(ref matchedQuery), "and it must match the query it was dispatched for");
    }

    [TestMethod]
    public void GetInstantResults_WhenTheSameDispatchTimesOutTwice_RequestsOneRefresh()
    {
        var storage = new FlowSettingsStorage(Path.GetTempPath());
        var host = new FlowPluginHost(storage, []);
        host.RegisterPlugin(new PluginPair
        {
            Metadata = new PluginMetadata { ID = "slow", Name = "Slow", ActionKeyword = "*" },
            Plugin = new SlowFlowPlugin(TimeSpan.FromMilliseconds(1500))
        });

        var refreshCalls = 0;
        PluginSdk.Services.SearchRefreshService.RefreshMatchingFunc = _ => Interlocked.Increment(ref refreshCalls);

        var dispatcher = new FlowQueryDispatcher(host);
        var provider = new FlowInstantResultProvider(dispatcher);
        var callers = Enumerable.Range(0, 2)
            .Select(_ => Task.Run(() => provider.GetInstantResults("same").ToList()))
            .ToArray();

        Task.WaitAll(callers);

        var deadline = Environment.TickCount64 + 4000;
        while (Volatile.Read(ref refreshCalls) == 0 && Environment.TickCount64 < deadline)
            Thread.Sleep(50);

        Assert.AreEqual(1, Volatile.Read(ref refreshCalls),
            "callers sharing one slow dispatch must schedule only one host refresh");
    }

    private sealed class SlowFlowPlugin(TimeSpan delay) : IAsyncPlugin
    {
        public Task InitAsync(PluginInitContext context) => Task.CompletedTask;

        public async Task<List<Result>> QueryAsync(Query query, CancellationToken token)
        {
            await Task.Delay(delay, token);
            return [new Result { Title = "Late Result" }];
        }
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
        Assert.IsNotNull(results[0].OnExecuteFunc);
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
