using Lertaro.Plugins.FlowLauncherBridge.Engine.JsonRpc;

namespace Lertaro.Plugins.FlowLauncherBridge.Tests.Engine.JsonRpc;

[TestClass]
public sealed class FlowPythonDownloaderTests
{
    private string _tempDir = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"flow_py_test_{Guid.NewGuid():N}");
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

    [TestMethod]
    public void EnsureSiteCustomizeInstalled_DoesNotInjectLegacySettingsRemapping()
    {
        FlowPythonDownloader.EnsureSiteCustomizeInstalled(_tempDir);

        var file = Path.Combine(_tempDir, "sitecustomize.py");
        Assert.IsTrue(File.Exists(file));

        var content = File.ReadAllText(file);
        Assert.IsFalse(content.Contains("_remap_settings_path", StringComparison.Ordinal));
        Assert.IsFalse(content.Contains("_hooked_stat", StringComparison.Ordinal));
        Assert.IsFalse(content.Contains("_hooked_exists", StringComparison.Ordinal));
        Assert.IsFalse(content.Contains("_hooked_open", StringComparison.Ordinal));
    }
}
