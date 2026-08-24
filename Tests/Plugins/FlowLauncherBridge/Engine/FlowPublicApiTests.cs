using Flow.Launcher.Plugin;
using Lertaro.Plugins.FlowLauncherBridge.Engine;

namespace Lertaro.Plugins.FlowLauncherBridge.Tests.Engine;

[TestClass]
public sealed class FlowPublicApiTests
{
    private string _tempDir = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "LertaroFlowApiTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [TestCleanup]
    public void Cleanup()
    {
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
}
