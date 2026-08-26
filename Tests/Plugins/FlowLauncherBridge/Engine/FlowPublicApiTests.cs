using Flow.Launcher.Plugin;
using Lertaro.Plugins.FlowLauncherBridge.Engine;

namespace Lertaro.Plugins.FlowLauncherBridge.Tests.Engine;

[TestClass]
[DoNotParallelize]
public sealed class FlowPublicApiTests
{
    private string _tempDir = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        PluginSdk.Services.ExplorerService.OpenDirectoryFunc = null;
        _tempDir = Path.Combine(Path.GetTempPath(), "LertaroFlowApiTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        PluginSdk.Services.ExplorerService.OpenDirectoryFunc = null;
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }

    public class SampleSettings
    {
        public string CustomValue { get; set; } = "initial";
    }

    [TestMethod]
    public void StorageMethods_LoadAndSaveCorrectly()
    {
        var metadata = new PluginMetadata { ID = "api-test-id", Name = "ApiTest" };
        var storage = new FlowSettingsStorage(_tempDir);
        var api = new FlowPublicApi(metadata, storage, () => []);

        var settings = api.LoadSettingJsonStorage<SampleSettings>();
        Assert.AreEqual("initial", settings.CustomValue);

        settings.CustomValue = "updated_by_api";
        api.SaveSettingJsonStorage<SampleSettings>();

        var reloaded = api.LoadSettingJsonStorage<SampleSettings>();
        Assert.AreEqual("updated_by_api", reloaded.CustomValue);
    }

    [TestMethod]
    public void FuzzySearch_MatchesQuerySubstring()
    {
        var metadata = new PluginMetadata { ID = "api-test-id", Name = "ApiTest" };
        var storage = new FlowSettingsStorage(_tempDir);
        var api = new FlowPublicApi(metadata, storage, () => []);

        var match = api.FuzzySearch("calc", "Calculator Application");
        Assert.IsTrue(match.Success);

        var noMatch = api.FuzzySearch("xyz", "Calculator Application");
        Assert.IsFalse(noMatch.Success);
    }

    [TestMethod]
    public void WindowMethods_DelegateToSearchWindowService()
    {
        var metadata = new PluginMetadata { ID = "api-test-id", Name = "ApiTest" };
        var storage = new FlowSettingsStorage(_tempDir);
        var api = new FlowPublicApi(metadata, storage, () => []);

        var isVisibleCalled = false;
        var hideCalled = false;
        var showCalled = false;
        var focusCalled = false;

        PluginSdk.Services.SearchWindowService.IsWindowVisibleFunc = () => { isVisibleCalled = true; return true; };
        PluginSdk.Services.SearchWindowService.HideWindowFunc = () => hideCalled = true;
        PluginSdk.Services.SearchWindowService.ShowWindowFunc = _ => showCalled = true;
        PluginSdk.Services.SearchWindowService.FocusQueryTextBoxFunc = () => focusCalled = true;

        try
        {
            Assert.IsTrue(api.IsMainWindowVisible());
            Assert.IsTrue(isVisibleCalled);

            api.HideMainWindow();
            Assert.IsTrue(hideCalled);

            api.ShowMainWindow();
            Assert.IsTrue(showCalled);

            api.FocusQueryTextBox();
            Assert.IsTrue(focusCalled);
        }
        finally
        {
            PluginSdk.Services.SearchWindowService.IsWindowVisibleFunc = null;
            PluginSdk.Services.SearchWindowService.HideWindowFunc = null;
            PluginSdk.Services.SearchWindowService.ShowWindowFunc = null;
            PluginSdk.Services.SearchWindowService.FocusQueryTextBoxFunc = null;
        }
    }

    [TestMethod]
    public void ChangeQuery_CustomKeyword_NormalizesDefaultKeyword()
    {
        var metadata = new PluginMetadata
        {
            ID = "audio-cowboy",
            Name = "AudioCowboy",
            ActionKeyword = "audio",
            ActionKeywords = ["audio"]
        };
        var storage = new FlowSettingsStorage(_tempDir);
        string? changedQuery = null;
        var api = new FlowPublicApi(metadata, storage, () => [], (q, _) => changedQuery = q);

        // Plugin sends "ac o " (hardcoded in plugin script) -> normalized to "audio o "
        api.ChangeQuery("ac o ", true);
        Assert.AreEqual("audio o ", changedQuery);

        // Plugin sends "ac" -> normalized to "audio"
        api.ChangeQuery("ac", true);
        Assert.AreEqual("audio", changedQuery);

        // Plugin sends "ac i " -> normalized to "audio i "
        api.ChangeQuery("ac i ", true);
        Assert.AreEqual("audio i ", changedQuery);

        // Plugin sends multi-level sub-menu "ac r MyProfile → " -> normalized to "audio r MyProfile → "
        api.ChangeQuery("ac r MyProfile → ", true);
        Assert.AreEqual("audio r MyProfile → ", changedQuery);

        // Plugin sends "" -> normalized to "audio "
        api.ChangeQuery(string.Empty, true);
        Assert.AreEqual("audio ", changedQuery);
    }

    [TestMethod]
    public void NormalizeQueryWithKeyword_RespectsOtherPluginKeywords()
    {
        var metadata = new PluginMetadata
        {
            ID = "audio-cowboy",
            Name = "AudioCowboy",
            ActionKeyword = "audio",
            ActionKeywords = ["audio"]
        };
        var storage = new FlowSettingsStorage(_tempDir);
        var api = new FlowPublicApi(metadata, storage, () => [], null, null, null, kw => kw.Equals("pm", StringComparison.OrdinalIgnoreCase));

        // When query matches another plugin's keyword "pm", it should not prepend "audio"
        var query = api.NormalizeQueryWithKeyword("pm install SomePlugin");
        Assert.AreEqual("pm install SomePlugin", query);
    }

    [TestMethod]
    public void FlowPublicApi_RaisesVisibilityChangedEvent()
    {
        var metadata = new PluginMetadata { ID = "TEST", Name = "Test" };
        var storage = new FlowSettingsStorage(_tempDir);
        var api = new FlowPublicApi(metadata, storage, () => []);

        bool? receivedVisibility = null;
        api.VisibilityChanged += (s, e) => receivedVisibility = e.IsVisible;

        api.RaiseVisibilityChanged(true);
        Assert.IsTrue(receivedVisibility);

        api.RaiseVisibilityChanged(false);
        Assert.IsFalse(receivedVisibility);
    }

    [TestMethod]
    public void FlowPublicApi_OpenDirectory_DelegatesToExplorerService()
    {
        var metadata = new PluginMetadata { ID = "TEST", Name = "Test" };
        var storage = new FlowSettingsStorage(_tempDir);
        var api = new FlowPublicApi(metadata, storage, () => []);

        string? receivedDir = null;
        string? receivedFile = null;
        PluginSdk.Services.ExplorerService.OpenDirectoryFunc = (dir, file) =>
        {
            receivedDir = dir;
            receivedFile = file;
        };

        api.OpenDirectory(@"C:\MyFolder", @"C:\MyFolder\file.txt");
        Assert.AreEqual(@"C:\MyFolder", receivedDir);
        Assert.AreEqual(@"C:\MyFolder\file.txt", receivedFile);
    }
}
