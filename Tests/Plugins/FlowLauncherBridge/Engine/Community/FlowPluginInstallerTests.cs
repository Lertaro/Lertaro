using Lertaro.PluginSdk.Services;
using Lertaro.Plugins.FlowLauncherBridge.Engine;
using Lertaro.Plugins.FlowLauncherBridge.Engine.Community;

namespace Lertaro.Plugins.FlowLauncherBridge.Tests.Engine.Community;

[TestClass]
[DoNotParallelize]
public sealed class FlowPluginInstallerTests
{
    private string _tempDir = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "LertaroFlowInstallTest_" + Guid.NewGuid().ToString("N"));
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
    public void IsInstalling_WhenNoInstallPending_ReturnsFalse() => Assert.IsFalse(FlowPluginInstaller.IsInstalling("random-id"));

    [TestMethod]
    public async Task DownloadAndInstallPluginAsync_WhenUrlIsEmpty_ReturnsFalse()
    {
        var storage = new FlowSettingsStorage(_tempDir);
        var host = new FlowPluginHost(storage);
        var plugin = new FlowCommunityPlugin { ID = "p1", Name = "TestPlugin", UrlDownload = "" };

        var result = await FlowPluginInstaller.DownloadAndInstallPluginAsync(plugin, host);

        Assert.IsFalse(result);
    }

    [TestMethod]
    public void SafeDeleteDirectory_WhenDirectoryExists_DeletesSuccessfully()
    {
        var testDir = Path.Combine(_tempDir, "deleteme");
        Directory.CreateDirectory(testDir);
        File.WriteAllText(Path.Combine(testDir, "a.txt"), "hello");

        FlowPluginInstaller.SafeDeleteDirectory(testDir);

        Assert.IsFalse(Directory.Exists(testDir));
    }

    [TestMethod]
    public void SafeDeleteDirectory_WhenFileIsLocked_WritesDeletedMarker()
    {
        var testDir = Path.Combine(_tempDir, "lockedDir");
        Directory.CreateDirectory(testDir);
        var lockedFile = Path.Combine(testDir, "locked.dll");
        File.WriteAllText(lockedFile, "dll bytes");

        using var fs = new FileStream(lockedFile, FileMode.Open, FileAccess.Read, FileShare.None);
        FlowPluginInstaller.SafeDeleteDirectory(testDir);
        Assert.IsTrue(Directory.Exists(testDir));
        Assert.IsTrue(File.Exists(Path.Combine(testDir, ".deleted")));
    }

    [TestMethod]
    public async Task InitializeAsync_WhenVersionedDirectoryExistsAndStandardIsDeleted_RenamesToStandard()
    {
        var pluginsDir = Path.Combine(_tempDir, "FlowData", "Plugins");
        Directory.CreateDirectory(pluginsDir);

        var oldLockedDir = Path.Combine(pluginsDir, "SamplePlugin");
        Directory.CreateDirectory(oldLockedDir);
        File.WriteAllText(Path.Combine(oldLockedDir, ".deleted"), "deleted");

        var versionedDir = Path.Combine(pluginsDir, "SamplePlugin-12345678");
        Directory.CreateDirectory(versionedDir);
        File.WriteAllText(Path.Combine(versionedDir, "plugin.json"), "{\"ID\":\"p1\",\"Name\":\"SamplePlugin\",\"ExecuteFileName\":\"p.dll\"}");

        var storage = new FlowSettingsStorage(_tempDir);
        var host = new FlowPluginHost(storage, [pluginsDir]);

        await host.InitializeAsync();

        Assert.IsFalse(File.Exists(Path.Combine(pluginsDir, "SamplePlugin", ".deleted")));
        Assert.IsTrue(Directory.Exists(Path.Combine(pluginsDir, "SamplePlugin")));
        Assert.IsTrue(File.Exists(Path.Combine(pluginsDir, "SamplePlugin", "plugin.json")));
        Assert.IsFalse(Directory.Exists(versionedDir));
    }
}
