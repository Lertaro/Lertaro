using Flow.Launcher.Plugin;
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
    public void GlyphInfo_WhenInstantiated_IsReferenceTypeAndPreservesProperties()
    {
        var glyph = new GlyphInfo("/Resources/#Segoe Fluent Icons", "\ue790");
        Assert.IsFalse(typeof(GlyphInfo).IsValueType, "GlyphInfo must be a reference type (record class) matching Flow.Launcher SDK");
        Assert.AreEqual("/Resources/#Segoe Fluent Icons", glyph.FontFamily);
        Assert.AreEqual("\ue790", glyph.Glyph);

        var result = new Result
        {
            Title = "Weather Beijing",
            Glyph = glyph
        };
        Assert.AreEqual(glyph, result.Glyph);
    }
}
