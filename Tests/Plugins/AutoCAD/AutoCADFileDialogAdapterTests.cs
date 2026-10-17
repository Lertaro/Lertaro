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

    // AutoCAD really does navigate on a path in the file-name box plus Enter -- the bug was that the path
    // stayed there afterwards, so the name the user meant to save was gone and could not be got back. These
    // pin when the remembered name may be written again; only the box's own text is consulted, because
    // anything else in it is either the dialog's doing or the user's, and overwriting that would be the same
    // damage this exists to undo.
    [TestMethod]
    public void TheLeftoverPathIsWhatGetsReplaced() =>
        Assert.IsTrue(AutoCADFileDialogAdapter.ShouldPutNameBack(boxNow: @"D:\a\", wrote: @"D:\a\"));

    [TestMethod]
    public void APathTheDialogNormalizedIsStillOurs() =>
        // Compared as paths: a dialog may drop the trailing separator or retype the drive, and that is not
        // somebody else putting text in the field. This comparison shape is what the first report of the fix
        // leaving the path in place came down to.
        Assert.IsTrue(AutoCADFileDialogAdapter.ShouldPutNameBack(boxNow: @"d:\a", wrote: @"D:\a\"));

    [TestMethod]
    public void AnEmptyBoxGoesBackToTheNameItHad() =>
        // Either the dialog cleared the field after navigating or the read got nothing back, and those look
        // the same from here. Both leave the user without the name they had, which is the thing to undo.
        Assert.IsTrue(AutoCADFileDialogAdapter.ShouldPutNameBack(boxNow: "", wrote: @"D:\a\"));

    [TestMethod]
    public void ABoxTheUserTypedIntoIsLeftAlone() =>
        Assert.IsFalse(AutoCADFileDialogAdapter.ShouldPutNameBack(boxNow: "泵房平面图", wrote: @"D:\a\"));

    [TestMethod]
    public void ABoxHoldingMoreThanThePathIsLeftAlone() =>
        // The folder plus something the dialog put on after it is not the path we wrote.
        Assert.IsFalse(AutoCADFileDialogAdapter.ShouldPutNameBack(boxNow: @"D:\a\Drawing1.dwg", wrote: @"D:\a\"));

    [TestMethod]
    public void APathTooLongToReadBackIsLeftAlone() =>
        // ReadTextBox caps at 1024 chars, so a longer path reads back short and compares unequal. The safe
        // direction for that mistake is to write nothing.
        Assert.IsFalse(AutoCADFileDialogAdapter.ShouldPutNameBack(boxNow: new string('路', 500), wrote: new string('路', 1024)));

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
