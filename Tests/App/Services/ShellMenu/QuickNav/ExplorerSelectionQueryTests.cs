using Lertaro.App.Services.ShellMenu.QuickNav;

namespace Lertaro.App.Tests.Services.ShellMenu.QuickNav;

[TestClass]
public sealed class ExplorerSelectionQueryTests
{
    public sealed class Document(object? items)
    {
        public object? SelectedItems => items;
    }

    public sealed class Selection(int count)
    {
        public int Count => count;
    }

    public sealed class FailedSelection
    {
        public int Count => throw new System.Runtime.InteropServices.COMException("View is navigating");
    }

    [TestMethod]
    [DataRow(0, true)]
    [DataRow(1, false)]
    [DataRow(-1, false)]
    public void OnlyAConfirmedZeroSelectionCountsAsEmpty(int count, bool expected) =>
        Assert.AreEqual(expected, ExplorerSelectionQuery.HasNoSelectedItems(new Document(new Selection(count))));

    [TestMethod]
    public void MissingOrFailingAutomationIsNotEmptySpace()
    {
        Assert.IsFalse(ExplorerSelectionQuery.HasNoSelectedItems(null));
        Assert.IsFalse(ExplorerSelectionQuery.HasNoSelectedItems(new Document(null)));
        Assert.IsFalse(ExplorerSelectionQuery.HasNoSelectedItems(new Document(new FailedSelection())));
        Assert.IsFalse(ExplorerSelectionQuery.HasNoSelectedItems(new object()));
    }

    [StaTestMethod]
    [DataRow(0L)]
    [DataRow(-1L)]
    public void UnknownWindowCannotAuthorizeAMenu(long handle) =>
        Assert.IsFalse(ExplorerSelectionQuery.IsActiveWindowFolderEmptySpace(new IntPtr(handle)));

    [StaTestMethod]
    public void NativeHitTestDistinguishesAnItemFromEmptySpace()
    {
        using var list = new System.Windows.Forms.ListView
        {
            Width = 300, Height = 200, View = System.Windows.Forms.View.Details
        };
        list.Columns.Add("Name", 250);
        var item = list.Items.Add("Example folder");
        var handle = list.Handle;
        var bounds = item.Bounds;
        Assert.IsGreaterThan(0, bounds.Height);
        var itemPoint = list.PointToScreen(new System.Drawing.Point(bounds.Left + 5, bounds.Top + bounds.Height / 2));
        var emptyPoint = list.PointToScreen(new System.Drawing.Point(250, 180));

        Assert.AreEqual<bool?>(true, DesktopIconHitTester.IsPointOnDesktopIcon(handle, itemPoint.X, itemPoint.Y));
        Assert.AreEqual<bool?>(false, DesktopIconHitTester.IsPointOnDesktopIcon(handle, emptyPoint.X, emptyPoint.Y));
        Assert.IsNull(DesktopIconHitTester.IsPointOnDesktopIcon(IntPtr.Zero, 0, 0));
    }
}
