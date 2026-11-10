using Lertaro.Plugins.CoreExtensions.FileDialog;

namespace Lertaro.Plugins.CoreExtensions.Tests.FileDialog;

// The classic GetOpenFileName/GetSaveFileName frame carries the same "write the path, then press Enter" pair as
// the modern one, and had the same foreground requirement on that Enter. The card that starts a navigation keeps
// the keyboard on purpose, so a classic dialog never moved either.
[TestClass]
public sealed class ClassicFileDialogAdapterTests
{
    // CanHandle is the gate the hook asks before it will hand a window to this adapter at all.
    [TestMethod]
    public void TheClassicFrameIsRecognized()
    {
        var adapter = new ClassicFileDialogAdapter();
        using var dialog = FakeCommonDialog.Classic();

        Assert.IsTrue(adapter.CanHandle(dialog.Hwnd, "#32770", "someapp"));
    }

    [TestMethod]
    public void EnterIsDeliveredWhileTheDialogIsNotForeground()
    {
        var adapter = new ClassicFileDialogAdapter();
        using var dialog = FakeCommonDialog.ClassicWithEdit();
        using var recorder = new EnterRecorder(dialog.Edit);

        Assert.AreNotEqual(FakeCommonDialog.GetForegroundWindow(), dialog.Hwnd,
            "the test window must not be the foreground one, or this pins nothing");

        Assert.IsTrue(adapter.NavigateTo(dialog.Hwnd, @"C:\"));
        Assert.IsTrue(recorder.WaitForEnter(TimeSpan.FromSeconds(5)),
            "NavigateTo queued no Enter into the field, so the dialog would never switch directory");
    }
}
