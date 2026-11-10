using Lertaro.Plugins.CoreExtensions.FileDialog;

namespace Lertaro.Plugins.CoreExtensions.Tests.FileDialog;

[TestClass]
public sealed class CommonDialogNavigationTests
{
    private const string Target = @"C:\Lertaro-Test-Target";

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void NavigateTo_DialogInForeground_PostsEnter(bool classic)
    {
        using var dialog = CreateDialog(classic);
        using var recorder = new EnterRecorder(dialog.Edit);

        Assert.IsTrue(Navigate(classic, dialog, () => { }, () => dialog.Hwnd));
        Assert.AreEqual(Target, DialogEditVerifier.ReadText(dialog.Edit));
        Assert.IsTrue(recorder.ReadQueuedEnter());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void NavigateTo_UserSwitchesAwayBeforeSubmission_DeclinesWithoutEnter(bool classic)
    {
        using var dialog = CreateDialog(classic);
        using var other = FakeCommonDialog.WithFolderEdit();
        using var recorder = new EnterRecorder(dialog.Edit);
        var foreground = dialog.Hwnd;

        var submitted = Navigate(classic, dialog, () =>
        {
            Assert.AreEqual(Target, DialogEditVerifier.ReadText(dialog.Edit));
            foreground = other.Hwnd;
        }, () => foreground);

        Assert.IsFalse(submitted);
        Assert.IsFalse(recorder.ReadQueuedEnter());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void NavigateTo_FieldChangedBeforeSubmission_PreservesTextWithoutEnter(bool classic)
    {
        using var dialog = CreateDialog(classic);
        using var recorder = new EnterRecorder(dialog.Edit);

        Assert.IsFalse(Navigate(classic, dialog, () => dialog.SetText("user edit"), () => dialog.Hwnd));
        Assert.AreEqual("user edit", DialogEditVerifier.ReadText(dialog.Edit));
        Assert.IsFalse(recorder.ReadQueuedEnter());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void NavigateTo_DialogClosedBeforeSubmission_Declines(bool classic)
    {
        using var dialog = CreateDialog(classic);

        Assert.IsFalse(Navigate(classic, dialog, dialog.Dispose, () => dialog.Hwnd));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void NavigateTo_ModalChildDisablesDialog_DeclinesWithoutEnter(bool classic)
    {
        using var dialog = CreateDialog(classic);
        using var modal = FakeCommonDialog.OwnedCard(dialog.Hwnd);
        using var recorder = new EnterRecorder(dialog.Edit);

        Assert.IsFalse(Navigate(classic, dialog, () => dialog.SetEnabled(false), () => modal.Hwnd));
        Assert.IsFalse(recorder.ReadQueuedEnter());
    }

    private static FakeCommonDialog CreateDialog(bool classic) =>
        classic ? FakeCommonDialog.ClassicWithEdit() : FakeCommonDialog.WithFolderEdit();

    private static bool Navigate(bool classic, FakeCommonDialog dialog, Action wait, Func<IntPtr> foreground) =>
        classic
            ? new ClassicFileDialogAdapter().NavigateTo(dialog.Hwnd, Target, wait, foreground)
            : new StandardFileDialogAdapter().NavigateTo(dialog.Hwnd, Target, wait, foreground);
}
