using Lertaro.PluginSdk.Services;
using Lertaro.Plugins.FlowLauncherBridge.Engine;
using Lertaro.Plugins.FlowLauncherBridge.Providers;

namespace Lertaro.Plugins.FlowLauncherBridge.Tests.Providers;

[TestClass]
[DoNotParallelize]
public sealed class FlowHighlightHelperTests
{
    private string _tempDir = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "LertaroFlowHighlightTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        FuzzyMatchService.GetHighlightMaskFunc = (text, term) =>
        {
            var mask = new bool[text.Length];
            var idx = text.IndexOf(term, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                for (var i = 0; i < term.Length && idx + i < text.Length; i++)
                    mask[idx + i] = true;
            }
            return mask;
        };
    }

    [TestCleanup]
    public void Cleanup()
    {
        FuzzyMatchService.GetHighlightMaskFunc = null;
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }

    [TestMethod]
    public void GetHighlightMask_TriggerKeywordWithInstallNoFilter_ReturnsAllFalse()
    {
        var storage = new FlowSettingsStorage(_tempDir);
        var host = new FlowPluginHost(storage, []);

        var mask = FlowHighlightHelper.GetHighlightMask(host, "flow", "FlowYouTube", "flow install");

        Assert.IsNotNull(mask);
        Assert.IsFalse(mask.Any(b => b));
    }

    [TestMethod]
    public void GetHighlightMask_TriggerKeywordWithInstallAndTerm_HighlightsOnlyTerm()
    {
        var storage = new FlowSettingsStorage(_tempDir);
        var host = new FlowPluginHost(storage, []);

        var mask = FlowHighlightHelper.GetHighlightMask(host, "flow", "FlowYouTube", "flow install tube");

        Assert.IsNotNull(mask);
        // "tube" in "FlowYouTube" starts at index 7 (FlowYou Tube)
        Assert.IsTrue(mask[7]);
        Assert.IsTrue(mask[8]);
        Assert.IsTrue(mask[9]);
        Assert.IsTrue(mask[10]);
        // "Flow" (index 0..3) should NOT be highlighted
        Assert.IsFalse(mask[0]);
        Assert.IsFalse(mask[1]);
        Assert.IsFalse(mask[2]);
        Assert.IsFalse(mask[3]);
    }
}
