namespace Lertaro.Plugins.QuickLookBridge.Tests;

// CanPreview for an existing path additionally depends on QuickLookPipeClient.IsAvailable(), which is
// environment-dependent (can't control whether the test machine has QuickLook running) -- so only the
// existence guard, which must short-circuit to false before ever touching the pipe, is asserted here.
// CreatePreview isn't exercised: constructing a FrameworkElement (even a bare Grid) requires an STA
// thread, which MSTest here doesn't run on (same reason OpenCommandPromptActionTests skips Execute()).
[TestClass]
public sealed class QuickLookPreviewProviderTests
{
    [TestMethod]
    public void RendersExternally_IsTrue() =>
        Assert.IsTrue(new QuickLookPreviewProvider().RendersExternally);

    [TestMethod]
    public void EndPreviewSession_DoesNotThrow() =>
        new QuickLookPreviewProvider().EndPreviewSession();

    [TestMethod]
    public void CanPreview_NonExistentFilePath_ReturnsFalse() =>
        Assert.IsFalse(new QuickLookPreviewProvider().CanPreview(@"Z:\definitely-not-a-real-lertaro-path.bin", isDir: false));

    [TestMethod]
    public void CanPreview_NonExistentDirectoryPath_ReturnsFalse() =>
        Assert.IsFalse(new QuickLookPreviewProvider().CanPreview(@"Z:\definitely-not-a-real-lertaro-dir", isDir: true));

    [TestMethod]
    public void CanPreview_EmptyPath_ReturnsFalse() =>
        Assert.IsFalse(new QuickLookPreviewProvider().CanPreview(string.Empty, isDir: false));

    [TestMethod]
    public void Name_IsNotEmpty() =>
        Assert.IsFalse(string.IsNullOrWhiteSpace(new QuickLookPreviewProvider().Name));

    [TestMethod]
    public void Priority_IsAboveEveryBuiltInProvider() =>
        Assert.IsGreaterThan(20, new QuickLookPreviewProvider().Priority); // 20 = ImagePreviewProvider, the highest built-in
}
