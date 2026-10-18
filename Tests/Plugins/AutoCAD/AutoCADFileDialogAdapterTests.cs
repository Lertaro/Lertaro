using Lertaro.PluginSdk.Abstractions.Plugins.WindowAdapters;

namespace Lertaro.Plugins.AutoCAD.Tests;

[TestClass]
public sealed class AutoCADFileDialogAdapterTests
{
    [TestMethod]
    public void DeadWindowIsRejectedBeforeControlInspection()
    {
        var adapter = new AutoCADFileDialogAdapter();

        Assert.IsFalse(adapter.CanHandle(IntPtr.Zero, "#32770", "acad"));
    }

    [TestMethod]
    public void DialogOperationsHandleDeadWindow()
    {
        var adapter = new AutoCADFileDialogAdapter();

        Assert.IsNull(adapter.GetCurrentPath(IntPtr.Zero));
        Assert.IsFalse(adapter.NavigateTo(IntPtr.Zero, @"C:\"));
        Assert.IsFalse(adapter.RestoreFocus(IntPtr.Zero));
        Assert.IsFalse(adapter.GetDockBounds(IntPtr.Zero, out var rect));
        Assert.AreEqual(default(AdapterRect), rect);

        // Guarded like GetDockBounds: EnumChildWindows with a null parent does not enumerate nothing, it
        // enumerates the desktop's own children, so an unguarded answer here would be some other window's
        // file list.
        Assert.IsFalse(adapter.TryGetFileListBounds(IntPtr.Zero, out var list));
        Assert.AreEqual(default(AdapterRect), list);
        Assert.IsFalse(adapter.TryGetTargetFieldBounds(IntPtr.Zero, out var field));
        Assert.AreEqual(default(AdapterRect), field);
    }

    [TestMethod]
    public void IsAnOpenSaveDialogAdapter()
    {
        var adapter = new AutoCADFileDialogAdapter();

        Assert.IsFalse(adapter.TargetIsFolderOnly);
        Assert.IsInstanceOfType<IFileDialogAdapter>(adapter);
        Assert.IsFalse(string.IsNullOrWhiteSpace(adapter.Name));
    }

    // AutoCAD really does navigate on a path in the file-name box plus Enter -- measured on the reporter's
    // machine, so nothing about the commit is tested or changed here. What is tested is the undo of the
    // leftover: the dialog leaves its own path sitting in the box after a folder jump, so this method puts
    // the name it found there back -- but only into a field that still holds exactly the path it wrote, and
    // only once the field has stopped changing under it, which is what keeps a slow machine from losing the
    // race and a fast one from watching a fixed wait.
    [TestMethod]
    public void OurPathHeldSteadyGetsTheNameBack() =>
        // Four identical reads at the poll interval is the settle window.
        Assert.IsTrue(AutoCADFileDialogAdapter.ClassifyReading(@"C:\Windows\", @"C:\Windows\", 4)
            == AutoCADFileDialogAdapter.BoxReading.PutNameBack);

    [TestMethod]
    public void AFieldThatIsStillMovingIsNotTouchedYet() =>
        // One reading of our path cannot tell "the dialog is done with it" from "the dialog is mid-write".
        Assert.IsTrue(AutoCADFileDialogAdapter.ClassifyReading(@"C:\Windows\", @"C:\Windows\", 3)
            == AutoCADFileDialogAdapter.BoxReading.KeepWaiting);

    [TestMethod]
    public void TextThatIsNotOursEndsTheAttempt()
    {
        // The user typed a name, or the dialog put something of its own there. Writing over either is the
        // damage the undo exists to prevent, so this is a stop, not a wait.
        Assert.IsTrue(AutoCADFileDialogAdapter.ClassifyReading(@"C:\Windows\", "泵房平面图", 9)
            == AutoCADFileDialogAdapter.BoxReading.LeaveAlone);
        // An emptied field likewise: the dialog cleared it, or the user selected all and deleted. The
        // evidence on the reporter's machine is that AutoCAD leaves the path sitting there instead, so this
        // branch is the unusual one -- and the field is no longer ours to write into.
        Assert.IsTrue(AutoCADFileDialogAdapter.ClassifyReading(@"C:\Windows\", "", 9)
            == AutoCADFileDialogAdapter.BoxReading.LeaveAlone);
    }

    [TestMethod]
    public void APathTooLongToReadBackIsLeftAlone() =>
        // ReadTextBox caps at 1024 chars, so a longer path reads back short and is not ours any more. The
        // safe direction for that mistake is to write nothing.
        Assert.IsTrue(AutoCADFileDialogAdapter.ClassifyReading(new string('路', 1024), new string('路', 500), 9)
            == AutoCADFileDialogAdapter.BoxReading.LeaveAlone);

    // The other half of the same undo: which targets have a leftover to undo at all. This runs in the
    // elevated hook process, where a drive the user mapped without elevation is not visible -- which is the
    // shape a real report came in as (S:\Rebuild\... navigated fine, and the name never came back).
    [TestMethod]
    public void AFolderThisProcessCanSeeIsUndone() =>
        Assert.IsTrue(AutoCADFileDialogAdapter.IsFolderNavigation(isFile: false, isDirectory: true, targetPath: @"D:\a"));

    [TestMethod]
    public void AFileThisProcessCanSeeIsNeverUndone()
    {
        // A picked file's path is the answer the save box wants, with or without an extension: putting the
        // old name back over it would delete what the user just asked to save.
        Assert.IsFalse(AutoCADFileDialogAdapter.IsFolderNavigation(isFile: true, isDirectory: false, targetPath: @"S:\图纸\泵房.dwg"));
        Assert.IsFalse(AutoCADFileDialogAdapter.IsFolderNavigation(isFile: true, isDirectory: false, targetPath: @"S:\notes\LICENSE"));
    }

    [TestMethod]
    public void APathOnADriveElevationCannotSeeIsJudgedByItsExtension()
    {
        // Neither probe can answer on a mapped drive this token does not have, and a real folder and an
        // extension-less file read identically there. The folder is the common case and the more likely one
        // for a path with no extension; a path that looks like a file is left as the user asked for it.
        Assert.IsTrue(AutoCADFileDialogAdapter.IsFolderNavigation(isFile: false, isDirectory: false, targetPath: @"S:\Rebuild\全新软件\参考文档\记录"));
        Assert.IsFalse(AutoCADFileDialogAdapter.IsFolderNavigation(isFile: false, isDirectory: false, targetPath: @"S:\Rebuild\说明.txt"));
    }
}
