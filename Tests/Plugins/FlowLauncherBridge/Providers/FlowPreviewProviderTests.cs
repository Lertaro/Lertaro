using System.Windows.Controls;
using Lertaro.PluginSdk.Services;
using Lertaro.Plugins.FlowLauncherBridge.Providers;

namespace Lertaro.Plugins.FlowLauncherBridge.Tests.Providers;

[TestClass]
public sealed class FlowPreviewProviderTests
{
    [TestMethod]
    public void CanPreview_ReturnsTrue_ForFlowPreviewScheme()
    {
        var provider = new FlowPreviewProvider();
        Assert.IsTrue(provider.CanPreview("flow-preview:12345", false));
        Assert.IsTrue(provider.CanPreview("__FLOW_PREVIEW__:12345", false));
        Assert.IsFalse(provider.CanPreview(@"C:\path\to\file.txt", false));
    }

    [StaTestMethod]
    public void CreatePreview_ReturnsControl_WhenRegistered()
    {
        var tb = new TextBlock { Text = "Dictionary Definition" };
        var uc = new UserControl { Content = tb };
        var key = PluginPreviewCache.Register("China", "MDict", new Lazy<UserControl>(() => uc));

        var provider = new FlowPreviewProvider();
        var element = provider.CreatePreview(key, false);

        Assert.IsNotNull(element);
        Assert.IsInstanceOfType<UserControl>(element);
    }

    [TestMethod]
    public void FlowPreview_ConfiguresWebView2UserDataFolderEnvironment()
    {
        _ = new FlowLauncherBridgePlugin();
        var envVar = Environment.GetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER");
        Assert.IsNotNull(envVar);
        Assert.IsTrue(envVar.EndsWith(Path.Combine("FlowData", "WebView"), StringComparison.OrdinalIgnoreCase));
    }
}
