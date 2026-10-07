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
    [DataRow("flow-preview:nonexistent")]
    [DataRow("flow-preview:test:MDict:00000000000000000000000000000000")]
    public void PluginPreviewCache_GetEntry_ReturnsNullForUnknownKey(string key)
    {
        var entry = PluginPreviewCache.GetEntry(key);
        Assert.IsNull(entry);
    }

    [StaTestMethod]
    public void RetainedEntry_RemainsUsableBeyondRecentEntryLimit()
    {
        var panel = new UserControl { Content = new TextBlock { Text = "definition" } };
        var entry = new PluginPreviewEntry("test", "MDict", new Lazy<UserControl>(() => panel));
        var key = PluginPreviewCache.Register(entry);
        for (var i = 0; i < 200; i++)
            PluginPreviewCache.Register("other", "other", new Lazy<UserControl>(() => new UserControl()));

        Assert.AreSame(panel, PluginPreviewCache.GetPreview(key));
        Assert.AreEqual(key, PluginPreviewCache.Register(entry));
        GC.KeepAlive(entry);
    }

    [TestMethod]
    public void UnownedEntry_IsReleasedBeyondRecentEntryLimit()
    {
        var key = PluginPreviewCache.Register("test", "MDict", new Lazy<UserControl>(() => new UserControl()));
        for (var i = 0; i < 200; i++)
            PluginPreviewCache.Register("other", "other", new Lazy<UserControl>(() => new UserControl()));

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.IsNull(PluginPreviewCache.GetEntry(key));
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
