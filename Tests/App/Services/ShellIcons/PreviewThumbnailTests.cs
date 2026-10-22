using System.IO;
using System.Windows.Media.Imaging;
using Lertaro.App.Services.ShellIcons;

namespace Lertaro.App.Tests.Services.ShellIcons;

// The preview seam is the shell's real-content thumbnail call, handed to plugins through
// IconService.GetThumbnail. These cover the delivery through the new bounded hop; the abandoned-call branch
// itself is the same wait shape ThumbnailCallGuardTests wedge deterministically, and no portable file makes
// the real shell answer slowly on cue.
[TestClass]
public sealed class PreviewThumbnailTests
{
    [TestMethod]
    public void TryGetPreviewThumbnail_SystemExecutable_ReturnsABitmap()
    {
        var notepad = Path.Combine(Environment.SystemDirectory, "notepad.exe");

        var image = ShellImageListInterop.TryGetPreviewThumbnail(notepad, 64);

        Assert.IsNotNull(image, "the shell answers a real path with its icon when it has no content thumbnail");
        var bitmap = Assert.IsInstanceOfType<BitmapSource>(image);
        Assert.IsGreaterThan(0, bitmap.PixelWidth);
    }

    [TestMethod]
    public void TryGetPreviewThumbnail_MissingPath_ReturnsNull()
    {
        var missing = Path.Combine(Environment.SystemDirectory, "lertaro-not-a-real-file.mp4");

        Assert.IsNull(ShellImageListInterop.TryGetPreviewThumbnail(missing, 64));
    }
}
