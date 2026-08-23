using Lertaro.Plugins.FlowLauncherBridge.Engine.JsonRpc;

namespace Lertaro.Plugins.FlowLauncherBridge.Tests.Engine.JsonRpc;

[TestClass]
public sealed class FlowPipManagerTests
{
    [TestMethod]
    public void EnsureFlowEnvironmentStubs_CreatesSettingsAndImagesDirectory()
    {
        FlowPipManager.EnsureFlowEnvironmentStubs();

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var settingsJson = Path.Combine(appData, "FlowLauncher", "Settings", "Settings.json");
        Assert.IsTrue(File.Exists(settingsJson));

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var imagesDir = Path.Combine(localAppData, "FlowLauncher", "Images");
        Assert.IsTrue(Directory.Exists(imagesDir));
    }
}
