using System.Windows.Controls;
using Lertaro.PluginSdk.Services;
using Lertaro.Plugins.FlowLauncherBridge.Engine;

namespace Lertaro.Plugins.FlowLauncherBridge.Tests.Engine;

[TestClass]
[DoNotParallelize]
public sealed class FlowPreviewEnvironmentTests
{
    private Func<string?>? _originalUserDataDirectory;
    private string? _originalWebView2Environment;

    [TestInitialize]
    public void CaptureProcessState()
    {
        _originalUserDataDirectory = UserDataService.GetUserDataDirectoryFunc;
        _originalWebView2Environment = Environment.GetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER");
    }

    [TestCleanup]
    public void RestoreProcessState()
    {
        UserDataService.GetUserDataDirectoryFunc = _originalUserDataDirectory;
        Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER", _originalWebView2Environment);
    }

    [StaTestMethod]
    public void CreatePreview_ScopesWebView2EnvironmentToFactory()
    {
        var userDataDirectory = Path.Combine(Path.GetTempPath(), "LertaroFlowPreviewEnvironmentTest");
        UserDataService.GetUserDataDirectoryFunc = () => userDataDirectory;
        string? valueDuringFactory = null;
        var factory = new Lazy<UserControl>(() =>
        {
            valueDuringFactory = Environment.GetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER");
            return new UserControl();
        });

        _ = FlowPreviewEnvironment.CreatePreview(factory);

        Assert.AreEqual(Path.Combine(userDataDirectory, "FlowData"), valueDuringFactory);
        Assert.AreEqual(_originalWebView2Environment, Environment.GetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER"));
    }

    [TestMethod]
    public void Enter_RestoresWebView2EnvironmentWhenScopeEnds()
    {
        var userDataDirectory = Path.Combine(Path.GetTempPath(), "LertaroFlowPreviewEnvironmentTest");
        UserDataService.GetUserDataDirectoryFunc = () => userDataDirectory;

        using (FlowPreviewEnvironment.Enter())
        {
            Assert.AreEqual(Path.Combine(userDataDirectory, "FlowData"),
                Environment.GetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER"));
        }

        Assert.AreEqual(_originalWebView2Environment, Environment.GetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER"));
    }
}
