using Lertaro.PluginSdk.Services;
using Lertaro.Plugins.FlowLauncherBridge.Engine;
using Lertaro.Plugins.FlowLauncherBridge.Engine.Community;
using Lertaro.Plugins.FlowLauncherBridge.Providers;

namespace Lertaro.Plugins.FlowLauncherBridge.Tests.Providers;

[TestClass]
[DoNotParallelize]
public sealed class FlowCommunityListHelperTests
{
    private string _tempDir = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "LertaroFlowListTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        TranslationService.LookupFunc = key => key;
    }

    [TestCleanup]
    public void Cleanup()
    {
        TranslationService.LookupFunc = key => $"[{key}]";
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }

    [TestMethod]
    public void QueryCommunityPlugins_WhenCacheEmpty_ReturnsLoadingItem()
    {
        var storage = new FlowSettingsStorage(_tempDir);
        var host = new FlowPluginHost(storage);

        var results = FlowCommunityListHelper.QueryCommunityPlugins(host, "flow", "", "flow list").ToList();

        Assert.HasCount(1, results);
        Assert.AreEqual("FlowLauncherBridge_CommunityLoadingTitle", results[0].Title);
    }

    [TestMethod]
    public void QueryCommunityPlugins_WhenCacheLoaded_ReturnsPluginItems()
    {
        var storage = new FlowSettingsStorage(_tempDir);
        var host = new FlowPluginHost(storage);

        var sampleList = new List<FlowCommunityPlugin>
        {
            new() { ID = "calc-id", Name = "Calculator", Author = "FlowTeam", Version = "1.0.0", Description = "A smart calc", Language = "C#", Website = "https://example.com/calc" },
            new() { ID = "weather-id", Name = "Weather", Author = "John", Version = "2.1.0", Description = "Check weather", Language = "Python", Website = "https://example.com/weather" }
        };

        var cachedField = typeof(FlowCommunityManifestService).GetField("_cachedPlugins", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var lastFetchField = typeof(FlowCommunityManifestService).GetField("_lastFetchTime", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

        cachedField.SetValue(null, sampleList);
        lastFetchField.SetValue(null, DateTime.UtcNow);

        try
        {
            var results = FlowCommunityListHelper.QueryCommunityPlugins(host, "flow", "", "flow list").ToList();
            Assert.HasCount(2, results);
            Assert.Contains("Calculator", results[0].Title);
            Assert.Contains("Weather", results[1].Title);

            var filtered = FlowCommunityListHelper.QueryCommunityPlugins(host, "flow", "calc", "flow list calc").ToList();
            Assert.HasCount(1, filtered);
            Assert.Contains("Calculator", filtered[0].Title);
            Assert.AreEqual("Execute", filtered[0].ActionType);
            Assert.IsNotNull(filtered[0].OnExecute);

            // If Calculator is already installed, it should be excluded
            var installedPair = new global::Flow.Launcher.Plugin.PluginPair
            {
                Metadata = new global::Flow.Launcher.Plugin.PluginMetadata { ID = "calc-id", Name = "Calculator", ActionKeyword = "calc" }
            };
            host.RegisterPlugin(installedPair);

            var afterInstallResults = FlowCommunityListHelper.QueryCommunityPlugins(host, "flow", "", "flow list").ToList();
            Assert.HasCount(1, afterInstallResults);
            Assert.Contains("Weather", afterInstallResults[0].Title);
        }
        finally
        {
            cachedField.SetValue(null, null);
            lastFetchField.SetValue(null, DateTime.MinValue);
        }
    }
}
