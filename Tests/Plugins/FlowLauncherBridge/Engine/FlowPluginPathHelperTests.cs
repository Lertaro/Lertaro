using Lertaro.Plugins.FlowLauncherBridge.Engine;

namespace Lertaro.Plugins.FlowLauncherBridge.Tests.Engine;

[TestClass]
public sealed class FlowPluginPathHelperTests
{
    [TestMethod]
    public void GetSettingsDirectory_UsesPluginSettingsFolder()
    {
        var path = FlowPluginPathHelper.GetSettingsDirectory(@"C:\Data", "SamplePlugin");

        Assert.AreEqual(Path.Combine(@"C:\Data", "FlowData", "Settings", "Plugins", "SamplePlugin"), path);
    }

    [TestMethod]
    public void GetCacheDirectory_UsesPluginCachesFolder()
    {
        var path = FlowPluginPathHelper.GetCacheDirectory(@"C:\Data", "SamplePlugin");

        Assert.AreEqual(Path.Combine(@"C:\Data", "FlowData", "Caches", "Plugins", "SamplePlugin"), path);
    }
}
