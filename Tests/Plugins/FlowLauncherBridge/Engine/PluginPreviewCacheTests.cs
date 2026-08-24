using System.Windows.Controls;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.FlowLauncherBridge.Tests.Engine;

[TestClass]
public sealed class PluginPreviewCacheTests
{
    [TestMethod]
    public void PluginPreviewCache_RegisterAndRetrieve_RoundtripsMetadata()
    {
        var factory = new Lazy<UserControl>(() => new UserControl());
        var key = PluginPreviewCache.Register("China", "MDict", factory);

        StringAssert.StartsWith(key, "flow-preview:");

        var entry = PluginPreviewCache.GetEntry(key);
        Assert.IsNotNull(entry);
        Assert.AreEqual("China", entry.Title);
        Assert.AreEqual("MDict", entry.PluginName);
    }

    [TestMethod]
    public void PluginPreviewCache_GetEntry_ReturnsNullForUnknownKey()
    {
        var entry = PluginPreviewCache.GetEntry("flow-preview:nonexistent");
        Assert.IsNull(entry);
    }

    [TestMethod]
    public void PluginPreviewCache_RegisterAndRetrieve_RoundtripsIconProvider()
    {
        var factory = new Lazy<UserControl>(() => new UserControl());
        var iconObj = "fake-icon-object";
        var key = PluginPreviewCache.Register("QR", "QRCodePlugin", factory, () => iconObj);

        var entry = PluginPreviewCache.GetEntry(key);
        Assert.IsNotNull(entry);
        Assert.AreEqual("QR", entry.Title);
        Assert.AreEqual("QRCodePlugin", entry.PluginName);
        Assert.AreEqual(iconObj, entry.GetIcon());
    }
}
