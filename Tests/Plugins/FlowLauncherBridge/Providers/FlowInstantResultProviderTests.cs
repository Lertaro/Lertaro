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

        Assert.HasCount(1, results);
        Assert.AreEqual("Flow Result Title", results[0].Title, "the pending placeholder is not a completed dispatch");
        Thread.Sleep(200);
        Assert.AreEqual(0, Volatile.Read(ref refreshCalls),
            "a dispatch that already returned its results must not also ask the host to re-run the search");
    }

    [TestMethod]
    public async Task GetInstantResults_WhenDispatchExceedsTimeout_RequestsOneRefreshAfterItLands()
    {
        // The timeout ran out, so a "query pending" placeholder is what the user is looking at. Once the
        // dispatch finishes, the host must re-run the query so the real results replace that placeholder.
        var storage = new FlowSettingsStorage(Path.GetTempPath());
        var host = new FlowPluginHost(storage, []);
        using var plugin = new GatedFlowPlugin();
        host.RegisterPlugin(new PluginPair
        {
            Metadata = new PluginMetadata { ID = "slow", Name = "Slow", ActionKeyword = "*" },
            Plugin = plugin
        });

        var refreshCalls = 0;
        var matchedQuery = string.Empty;
        var refreshed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        PluginSdk.Services.SearchRefreshService.RefreshMatchingFunc = predicate =>
        {
            if (predicate("dev"))
            {
                Volatile.Write(ref matchedQuery, "dev");
                Interlocked.Increment(ref refreshCalls);
                refreshed.TrySetResult();
            }
        };

        var dispatcher = new FlowQueryDispatcher(host);
        var provider = new FlowInstantResultProvider(dispatcher);

        var results = provider.GetInstantResults("dev").ToList();

        Assert.HasCount(1, results, "the caller should get the pending placeholder, not the late results");
        Assert.AreEqual("None", results[0].ActionType);

        plugin.Complete();
        await refreshed.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.AreEqual(1, Volatile.Read(ref refreshCalls), "the late dispatch must ask for exactly one refresh");
        Assert.AreEqual("dev", Volatile.Read(ref matchedQuery), "and it must match the query it was dispatched for");
    }

    [TestMethod]
    public async Task GetInstantResults_WhenTheSameDispatchTimesOutTwice_RequestsOneRefresh()
    {
        var storage = new FlowSettingsStorage(Path.GetTempPath());
        var host = new FlowPluginHost(storage, []);
        using var plugin = new GatedFlowPlugin();
        host.RegisterPlugin(new PluginPair
        {
            Metadata = new PluginMetadata { ID = "slow", Name = "Slow", ActionKeyword = "*" },
            Plugin = plugin
        });

        var refreshCalls = 0;
        var refreshed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        PluginSdk.Services.SearchRefreshService.RefreshMatchingFunc = _ =>
        {
            Interlocked.Increment(ref refreshCalls);
            refreshed.TrySetResult();
        };

        var dispatcher = new FlowQueryDispatcher(host);
        var provider = new FlowInstantResultProvider(dispatcher);
        var callers = Enumerable.Range(0, 2)
            .Select(_ => Task.Run(() => provider.GetInstantResults("same").ToList()))
            .ToArray();

        await Task.WhenAll(callers).WaitAsync(TimeSpan.FromSeconds(5));
        foreach (var caller in callers)
            Assert.AreEqual("None", caller.Result.Single().ActionType);
        plugin.Complete();
        await refreshed.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.AreEqual(1, Volatile.Read(ref refreshCalls),
            "callers sharing one slow dispatch must schedule only one host refresh");
    }

    private sealed class GatedFlowPlugin : IAsyncPlugin, IDisposable
    {
        private readonly TaskCompletionSource<List<Result>> _results = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task InitAsync(PluginInitContext context) => Task.CompletedTask;
        public Task<List<Result>> QueryAsync(Query query, CancellationToken token) => _results.Task.WaitAsync(token);
        public void Complete() => _results.TrySetResult([new Result { Title = "Late Result" }]);
        public void Dispose() => _results.TrySetCanceled();
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

    // The bridge's own word has to stay first, and every loaded Flow plugin's ActionKeyword has to be
    // published with it: ParseQuery dispatches on that first word, so the host must strip it off the file
    // search or "gh lertaro" searches files for "gh lertaro". A "*" plugin answers to everything and has no
    // word of its own, so it contributes none.
    [TestMethod]
    public void QueryTriggerKeywords_PublishesTheBridgeWordAndEachPluginsActionKeyword()
    {
        var host = new FlowPluginHost(new FlowSettingsStorage(Path.GetTempPath()), []);
        host.RegisterPlugin(new PluginPair
        {
            Metadata = new PluginMetadata { ID = "gh", Name = "GitHub", ActionKeyword = "gh" },
            Plugin = new FakeFlowPlugin()
        });
        host.RegisterPlugin(new PluginPair
        {
            Metadata = new PluginMetadata { ID = "any", Name = "Global", ActionKeyword = "*" },
            Plugin = new FakeFlowPlugin()
        });

        var words = new FlowInstantResultProvider(new FlowQueryDispatcher(host), host).QueryTriggerKeywords;

        Assert.HasCount(2, words);
        Assert.AreEqual("flow", words[0]);
        Assert.Contains("gh", words);
        Assert.DoesNotContain("*", words);
    }

    // A keyword only disabled plugins answer to is not offered at dispatch time, so stripping it would take
    // the word out of the user's file search with nothing on screen to show for it.
    [TestMethod]
    public void QueryTriggerKeywords_SkipsAKeywordOnlyDisabledPluginsAnswerTo()
    {
        var host = new FlowPluginHost(new FlowSettingsStorage(Path.GetTempPath()), []);
        host.RegisterPlugin(new PluginPair
        {
            Metadata = new PluginMetadata { ID = "off", Name = "Disabled", ActionKeyword = "off", Disabled = true },
            Plugin = new FakeFlowPlugin()
        });

        var words = new FlowInstantResultProvider(new FlowQueryDispatcher(host), host).QueryTriggerKeywords;

        Assert.HasCount(1, words);
        Assert.DoesNotContain("off", words);
    }

    [TestMethod]
    public void QueryTriggerKeywords_DoesNotRepeatTheBridgeWordWhenAPluginAlsoUsesIt()
    {
        var host = new FlowPluginHost(new FlowSettingsStorage(Path.GetTempPath()), []);
        host.RegisterPlugin(new PluginPair
        {
            Metadata = new PluginMetadata { ID = "flow2", Name = "Flow Clone", ActionKeyword = "flow" },
            Plugin = new FakeFlowPlugin()
        });

        var words = new FlowInstantResultProvider(new FlowQueryDispatcher(host), host).QueryTriggerKeywords;

        Assert.HasCount(1, words);
        Assert.AreEqual("flow", words[0]);
    }
}
