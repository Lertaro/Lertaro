using System.Text.Json;
using Flow.Launcher.Plugin;

namespace Lertaro.Plugins.FlowLauncherBridge.Tests.Flow.Launcher.Plugin;

[TestClass]
public sealed class ResultTests
{
    [TestMethod]
    public void Clone_CopiesPreviewAndNewApiStateWithoutSharingMutablePreview()
    {
        var result = new Result { PreviewVisibility = PreviewVisibility.Always,
            Preview = new() { Description = "original", ContentType = PreviewContentType.Markdown },
            SubTitleHighlightData = new[] { 1, 2 } };
        var clone = result.Clone();
        clone.Preview.Description = "changed";
        Assert.AreEqual("original", result.Preview.Description);
        Assert.AreNotSame(new Result().Preview, new Result().Preview);
        Assert.AreEqual(PreviewVisibility.Always, clone.PreviewVisibility);
        Assert.AreEqual(PreviewContentType.Markdown, clone.Preview.ContentType);
        CollectionAssert.AreEqual(new[] { 1, 2 }, clone.SubTitleHighlightData!.ToArray());
    }

    [TestMethod]
    public void Json_NewPreviewEnumsUseUpstreamSpelling()
    {
        var result = JsonSerializer.Deserialize<Result>("""{"PreviewVisibility":"never","Preview":{"ContentType":"markdown"}}""")!;
        Assert.AreEqual(PreviewVisibility.Never, result.PreviewVisibility);
        Assert.AreEqual(PreviewContentType.Markdown, result.Preview.ContentType);
        Assert.Contains("\"markdown\"", JsonSerializer.Serialize(result));
        var glyph = new GlyphInfo("Segoe MDL2 Assets", "\uE10F");
        result.SetGlyph(glyph);
        Assert.AreSame(glyph, result.Glyph);
    }
}
