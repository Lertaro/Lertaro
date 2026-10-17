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
}
