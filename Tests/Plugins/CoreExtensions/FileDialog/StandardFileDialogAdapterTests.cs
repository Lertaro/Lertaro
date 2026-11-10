using Lertaro.Plugins.CoreExtensions.FileDialog;

namespace Lertaro.Plugins.CoreExtensions.Tests.FileDialog;

// This probe decides whether the inline card hides files, so it is pinned against the control shapes measured
// off live dialogs: an Open/Save dialog carries the shell's file-name ComboBoxEx32 #1148 (whose own child Edit
// repeats that same id), a FOS_PICKFOLDERS picker has no #1148 at all and puts a plain Edit #1152 in that slot.
// Real windows, because the probe reads real Win32 class names and dialog control ids.
[TestClass]
public sealed class StandardFileDialogAdapterTests
{
    [TestMethod]
    public void TheOpenDialogShapeTakesFiles()
    {
        var adapter = new StandardFileDialogAdapter();
        using var dialog = FakeCommonDialog.WithNameCombo();

        Assert.IsTrue(adapter.CanHandle(dialog.Hwnd, "#32770", "notepad"));
        Assert.IsFalse(adapter.TargetIsFolderOnly);
    }

    [TestMethod]
    public void TheBrowseForFolderShapeTakesOnlyFolders()
    {
        var adapter = new StandardFileDialogAdapter();
        using var dialog = FakeCommonDialog.WithFolderEdit();

        Assert.IsTrue(adapter.CanHandle(dialog.Hwnd, "#32770", "someapp"));
        Assert.IsTrue(adapter.TargetIsFolderOnly);
    }

    // The probe asks both halves of its question on purpose: a frame that has no #1148 to begin with must not
    // be guessed at, because guessing here is what hides every file from someone trying to pick one.
    [TestMethod]
    public void AFrameWithNeitherControlIsNotGuessedAt()
    {
        var adapter = new StandardFileDialogAdapter();
        using var dialog = FakeCommonDialog.WithNeitherNameField();

        Assert.IsTrue(adapter.CanHandle(dialog.Hwnd, "#32770", "someapp"));
        Assert.IsFalse(adapter.TargetIsFolderOnly);
    }

    // The address bar is not the only tell. A picker whose band is collapsed or absent still draws the
    // DirectUI shell view, and leaving it unclaimed was how tssh's zenity folder picker fell to the legacy
    // browse adapter -- which that dialog ignores, silently.
    [TestMethod]
    public void TheModernShellViewWithoutAnAddressBarIsStillClaimed()
    {
        var adapter = new StandardFileDialogAdapter();
        using var dialog = FakeCommonDialog.ModernFolderPicker();

        Assert.IsTrue(adapter.CanHandle(dialog.Hwnd, "#32770", "tssh"));
    }

    // The owned card may keep the keyboard while its host receives the path.
    [TestMethod]
    public void EnterIsDeliveredWhileTheOwnedCardIsForeground()
    {
        var adapter = new StandardFileDialogAdapter();
        using var dialog = FakeCommonDialog.WithFolderEdit();
        using var card = FakeCommonDialog.OwnedCard(dialog.Hwnd);
        using var recorder = new EnterRecorder(dialog.Edit);

        Assert.IsTrue(adapter.NavigateTo(dialog.Hwnd, @"C:\Lertaro-Test-Target", () => { }, () => card.Hwnd));
        Assert.IsTrue(recorder.ReadQueuedEnter(),
            "NavigateTo queued no Enter into the field, so the dialog would never switch directory");
    }
}
