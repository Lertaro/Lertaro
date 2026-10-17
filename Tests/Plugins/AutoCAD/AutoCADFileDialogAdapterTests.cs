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
    public void ABoxTheDialogMovedOnFromIsLeftAlone() =>
        // Whatever the field holds once this method's path is gone out of it is not ours to replace: the
        // dialog may have emptied it after the navigation, and it may have been left holding something else.
        Assert.IsFalse(AutoCADFileDialogAdapter.ShouldPutNameBack(boxNow: "", wrote: @"D:\a\"));

    [TestMethod]
    public void ABoxTheUserTypedIntoIsLeftAlone() =>
        Assert.IsFalse(AutoCADFileDialogAdapter.ShouldPutNameBack(boxNow: "泵房平面图", wrote: @"D:\a\"));

    [TestMethod]
    public void APathTooLongToReadBackIsLeftAlone() =>
        // ReadTextBox caps at 1024 chars, so a longer path reads back short and compares unequal. The safe
        // direction for that mistake is to write nothing.
        Assert.IsFalse(AutoCADFileDialogAdapter.ShouldPutNameBack(boxNow: new string('路', 500), wrote: new string('路', 1024)));
}
