using Lertaro.Plugins.CoreExtensions.FileDialog;

namespace Lertaro.Plugins.CoreExtensions.Tests.FileDialog;

// The legacy browse dialog and the modern folder picker share a shape (a #32770 frame with a SysTreeView32
// id=100), and the legacy adapter's only lever, BFFM_SETSELECTION, means nothing to the modern one. Claiming
// it is a navigation that reports success and does nothing, so the two are told apart here.
[TestClass]
public sealed class FolderBrowserDialogAdapterTests
{
    [TestMethod]
    public void TheModernFolderPickerIsNotClaimed()
    {
        var adapter = new FolderBrowserDialogAdapter();
        using var dialog = FakeCommonDialog.ModernFolderPicker();

        Assert.IsFalse(adapter.CanHandle(dialog.Hwnd, "#32770", "tssh"));
    }

    [TestMethod]
    public void TheLegacyBrowseForFolderIsClaimed()
    {
        var adapter = new FolderBrowserDialogAdapter();
        using var dialog = FakeCommonDialog.LegacyBrowseForFolder();

        Assert.IsTrue(adapter.CanHandle(dialog.Hwnd, "#32770", "legacyapp"));
        Assert.IsTrue(adapter.TargetIsFolderOnly);
    }
}
