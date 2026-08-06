using Lertaro.Plugins.CoreExtensions.Preview.Providers;

namespace Lertaro.Plugins.CoreExtensions.Tests.Preview.Providers;

[TestClass]
public sealed class PePreviewProviderTests
{
    private static readonly PePreviewProvider Provider = new();

    [TestMethod]
    public void CanPreview_ExeFile_ReturnsTrue() => Assert.IsTrue(Provider.CanPreview(@"C:\app.exe", isDir: false));

    [TestMethod]
    public void CanPreview_DllFile_ReturnsTrue() => Assert.IsTrue(Provider.CanPreview(@"C:\lib.dll", isDir: false));

    [TestMethod]
    public void CanPreview_ExtensionMatchIsCaseInsensitive() => Assert.IsTrue(Provider.CanPreview(@"C:\App.EXE", isDir: false));

    [TestMethod]
    public void CanPreview_OtherExtension_ReturnsFalse() => Assert.IsFalse(Provider.CanPreview(@"C:\readme.txt", isDir: false));

    [TestMethod]
    public void CanPreview_Directory_ReturnsFalseEvenWithExeExtension() => Assert.IsFalse(Provider.CanPreview(@"C:\app.exe", isDir: true));
}
