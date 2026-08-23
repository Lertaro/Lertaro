using Flow.Launcher.Plugin;
using Lertaro.PluginSdk.Services;
using Lertaro.Plugins.FlowLauncherBridge.Engine;
using Lertaro.Plugins.FlowLauncherBridge.Engine.Community;
using Lertaro.Plugins.FlowLauncherBridge.Providers;

namespace Lertaro.Plugins.FlowLauncherBridge.Tests.Providers;

[TestClass]
[DoNotParallelize]
public sealed class FlowCommunityUpdateHelperTests
{
    private string _tempDir = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "LertaroFlowUpdateTest_" + Guid.NewGuid().ToString("N"));
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
    public void IsNewerVersion_ComparesCorrectly()
    {
        Assert.IsTrue(FlowCommunityUpdateHelper.IsNewerVersion("1.1.0", "1.0.0"));
        Assert.IsTrue(FlowCommunityUpdateHelper.IsNewerVersion("2.0.0", "1.9.9"));
        Assert.IsFalse(FlowCommunityUpdateHelper.IsNewerVersion("1.0.0", "1.0.0"));
        Assert.IsFalse(FlowCommunityUpdateHelper.IsNewerVersion("1.0.0", "1.1.0"));
        Assert.IsTrue(FlowCommunityUpdateHelper.IsNewerVersion("1.0.0", ""));
        Assert.IsFalse(FlowCommunityUpdateHelper.IsNewerVersion("", "1.0.0"));
    }

    [TestMethod]
    public void QueryPluginUpdates_WhenNoUpdates_ReturnsNoUpdatesItem()
    {
        var storage = new FlowSettingsStorage(_tempDir);
        var host = new FlowPluginHost(storage);

        var sampleList = new List<FlowCommunityPlugin>
        {
            new() { ID = "calc-id", Name = "Calculator", Version = "1.0.0" }
        };

        var cachedField = typeof(FlowCommunityManifestService).GetField("_cachedPlugins", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var lastFetchField = typeof(FlowCommunityManifestService).GetField("_lastFetchTime", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

        cachedField.SetValue(null, sampleList);
        lastFetchField.SetValue(null, DateTime.UtcNow);

        try
        {
            // Local is 1.0.0, online is 1.0.0 -> no update
            host.RegisterPlugin(new PluginPair
            {
                Metadata = new PluginMetadata { ID = "calc-id", Name = "Calculator", Version = "1.0.0" }
            });

            var results = FlowCommunityUpdateHelper.QueryPluginUpdates(host, "flow", "", "flow update").ToList();

            Assert.HasCount(1, results);
            Assert.AreEqual("FlowLauncherBridge_NoUpdatesTitle", results[0].Title);
        }
        finally
        {
            cachedField.SetValue(null, null);
            lastFetchField.SetValue(null, DateTime.MinValue);
        }
    }

    [TestMethod]
    public void QueryPluginUpdates_WhenUpdateAvailable_ReturnsUpdatablePlugin()
    {
        var storage = new FlowSettingsStorage(_tempDir);
        var host = new FlowPluginHost(storage);

        var sampleList = new List<FlowCommunityPlugin>
        {
            new() { ID = "calc-id", Name = "Calculator", Author = "FlowTeam", Version = "2.0.0", Description = "Calc V2", UrlDownload = "https://example.com/calc.zip" }
        };

        var cachedField = typeof(FlowCommunityManifestService).GetField("_cachedPlugins", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var lastFetchField = typeof(FlowCommunityManifestService).GetField("_lastFetchTime", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

        cachedField.SetValue(null, sampleList);
        lastFetchField.SetValue(null, DateTime.UtcNow);

        try
        {
            // Local is 1.0.0, online is 2.0.0 -> update available
            host.RegisterPlugin(new PluginPair
            {
                Metadata = new PluginMetadata { ID = "calc-id", Name = "Calculator", Version = "1.0.0" }
            });

            var results = FlowCommunityUpdateHelper.QueryPluginUpdates(host, "flow", "", "flow update").ToList();

            Assert.HasCount(1, results);
            Assert.Contains("Calculator", results[0].Title);
            Assert.Contains("v1.0.0 → v2.0.0", results[0].Title);
            Assert.AreEqual("Execute", results[0].ActionType);
            Assert.IsNotNull(results[0].OnExecute);
        }
        finally
        {
            cachedField.SetValue(null, null);
            lastFetchField.SetValue(null, DateTime.MinValue);
        }
    }
}
