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
    }

    [TestMethod]
    public void TargetIsFolderOnly_IsFalse() =>
        // Unlike the archive-tool adapters, whose destination field can only hold a folder, this one is an
        // Open/Save file-name box: a picked file must arrive as that file, not as its parent folder.
        Assert.IsFalse(new AutoCADFileDialogAdapter().TargetIsFolderOnly);
}
