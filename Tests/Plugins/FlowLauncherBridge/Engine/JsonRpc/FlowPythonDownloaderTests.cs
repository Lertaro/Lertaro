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
    public void EnsureSiteCustomizeInstalled_ReplacesLegacySettingsRemappingWithImportHook()
    {
        var file = Path.Combine(_tempDir, "sitecustomize.py");
        File.WriteAllText(file, "def _remap_settings_path(path): return path\n_hooked_stat = _hooked_exists = _hooked_open = None");

        FlowPythonDownloader.EnsureSiteCustomizeInstalled(_tempDir);

        Assert.IsTrue(File.Exists(file));

        var content = File.ReadAllText(file);
        StringAssert.Contains(content, "class _FloxMetaFinder");
        StringAssert.Contains(content, "sys.meta_path.insert(0, _FloxMetaFinder())");
        Assert.IsFalse(content.Contains("_remap_settings_path", StringComparison.Ordinal));
        Assert.IsFalse(content.Contains("_hooked_stat", StringComparison.Ordinal));
        Assert.IsFalse(content.Contains("_hooked_exists", StringComparison.Ordinal));
        Assert.IsFalse(content.Contains("_hooked_open", StringComparison.Ordinal));
    }
}
