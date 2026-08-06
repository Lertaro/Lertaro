using Lertaro.Plugins.CoreExtensions.Preview.Handlers;
using Lertaro.Plugins.CoreExtensions.Preview.Providers;

namespace Lertaro.Plugins.CoreExtensions.Tests.Preview.Providers;

[TestClass]
public sealed class MediaPreviewProviderTests
{
    private static readonly MediaPreviewProvider Provider = new();

    [TestMethod]
    public void CanPreview_Mp4File_ReturnsTrue() => Assert.IsTrue(Provider.CanPreview(@"C:\movie.mp4", isDir: false));

    [TestMethod]
    public void CanPreview_Mp3File_ReturnsTrue() => Assert.IsTrue(Provider.CanPreview(@"C:\song.mp3", isDir: false));

    [TestMethod]
    public void CanPreview_ExtensionMatchIsCaseInsensitive() => Assert.IsTrue(Provider.CanPreview(@"C:\Movie.MP4", isDir: false));

    [TestMethod]
    public void CanPreview_UnsupportedContainer_ReturnsFalse() => Assert.IsFalse(Provider.CanPreview(@"C:\clip.mkv", isDir: false));

    [TestMethod]
    public void CanPreview_OtherExtension_ReturnsFalse() => Assert.IsFalse(Provider.CanPreview(@"C:\readme.txt", isDir: false));

    [TestMethod]
    public void CanPreview_Directory_ReturnsFalseEvenWithMediaExtension() => Assert.IsFalse(Provider.CanPreview(@"C:\movie.mp4", isDir: true));

    [TestMethod]
    public void Priority_AboveShellPreviewHandlerProvider() => Assert.IsGreaterThan(new ShellPreviewHandlerProvider().Priority, Provider.Priority);
}
