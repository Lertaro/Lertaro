using Lertaro.App.Views.InlineSearchWindow.Helpers;

namespace Lertaro.App.Tests.Views.InlineSearchWindow.Helpers;

[TestClass]
public sealed class InlineSearchWindowPositionerTests
{
    [TestMethod]
    public void CalculateDockedWidth_UsesTwoThirdsOfTargetWindow() => Assert.AreEqual(800, InlineSearchWindowPositioner.CalculateDockedWidth(1200));

    [TestMethod]
    public void CalculateDockedWidth_DoesNotApplyMinimumWidth() => Assert.AreEqual(200, InlineSearchWindowPositioner.CalculateDockedWidth(300));

    [TestMethod]
    public void CalculateDesktopWidth_UsesTwentyPercentOfWorkingArea() => Assert.AreEqual(384, InlineSearchWindowPositioner.CalculateDesktopWidth(1920));

    // The numbers below are a real WPS file dialog, measured off a live one: dialog 480..1440, file-name
    // box 783..1405, card 640 wide. Centering it put the card's left edge 143px left of the box.
    private static Core.Hook.ExplorerTracker.RECT Rect(int left, int right) =>
        new() { Left = left, Top = 272, Right = right, Bottom = 920 };

    [TestMethod]
    public void CalculateDialogPhysLeft_CentersOnTheDialogWhenNoFieldIsNamed() =>
        Assert.AreEqual(640, InlineSearchWindowPositioner.CalculateDialogPhysLeft(
            Rect(480, 1440), null, 640, 12, 0));

    [TestMethod]
    public void CalculateDialogPhysLeft_HangsTheCardOffTheFieldsRightEdge() =>
        // 1405 - 640 + 12: the +12 is the transparent XAML margin, so it is the card's *visible* right edge
        // that meets the field's, which is what the user reads as "under the box".
        Assert.AreEqual(777, InlineSearchWindowPositioner.CalculateDialogPhysLeft(
            Rect(480, 1440), Rect(783, 1405), 640, 12, 0));

    // The three tests below use two real dialogs, measured off live windows with Lertaro's own probes at
    // 96 DPI (so physical px == DIP):
    //
    //   Firefox's 另存为  -- #32770, GetWindowRect 184,314..1055,931, DWM frame 191,314..1048,924, its
    //                        shell-view pane 192,404..1047,862 (the 保存 button sits at 839,879, i.e. below
    //                        that pane), and its file list SHELLDLL_DefView at 352,438..1047,788.
    //   WPS's 上传到云     -- 187,310..1147,958, confirm group kd::KDConfirmGroup 388,898..1147,958, file
    //                        list KcfdContentWidget 396,394..1147,818.
    //
    // Card width is the existing 2/3 of the dock rect: 571 for the Firefox dialog (857 wide), 640 for WPS.
    private static Core.Hook.ExplorerTracker.RECT Box(int left, int top, int right, int bottom) =>
        new() { Left = left, Top = top, Right = right, Bottom = bottom };

    private static Core.Hook.ExplorerTracker.InlineCardHang Hang(
        Core.Hook.ExplorerTracker.RECT dock,
        Core.Hook.ExplorerTracker.RECT? buttonRow = null,
        Core.Hook.ExplorerTracker.RECT? fileList = null) =>
        Core.Hook.ExplorerTracker.InlineCardHang.Resolve(dock, buttonRow, fileList);

    [TestMethod]
    public void ADialogCardHangingBelowStartsAtItsButtonRowsTopLeftCorner()
    {
        var dock = Box(191, 314, 1048, 924);
        var hang = Hang(dock, Box(192, 862, 1055, 931), Box(352, 438, 1047, 788));

        var left = InlineSearchWindowPositioner.CalculatePhysLeft(true, true, dock, hang, null, 571, 12, 0);
        var top = InlineSearchWindowPositioner.CalculatePhysTop(true, hang, dock, 12, 0);

        // The window starts one margin out, so what the user sees lands on the row's own corner: left 192,
        // top 862 -- left-aligned with the file list above it rather than with the 保存 button at 839.
        Assert.AreEqual(180, left);
        Assert.AreEqual(850, top);
    }

    [TestMethod]
    public void ADialogCardWithNoRoomBelowHangsOffItsFileListsTopRightCorner()
    {
        var dock = Box(191, 314, 1048, 924);
        var hang = Hang(dock, Box(192, 862, 1055, 931), Box(352, 438, 1047, 788));

        var left = InlineSearchWindowPositioner.CalculatePhysLeft(true, false, dock, hang, null, 571, 12, 0);
        var top = InlineSearchWindowPositioner.CalculatePhysTop(false, hang, dock, 12, 0);

        // Visible right edge = 488 + 571 - 12 = 1047 and visible top = 426 + 12 = 438, i.e. the card's top
        // right corner on the list's, hanging down over it -- the same corner a card over a file manager's
        // window uses, which is the point: the two are meant to look alike.
        Assert.AreEqual(488, left);
        Assert.AreEqual(426, top);
    }

    [TestMethod]
    public void TheSameTwoCornersComeOutOnAWpsDialog()
    {
        var dock = Box(187, 310, 1147, 958);
        var hang = Hang(dock, Box(388, 898, 1147, 958), Box(396, 394, 1147, 818));

        Assert.AreEqual(376, InlineSearchWindowPositioner.CalculatePhysLeft(true, true, dock, hang, null, 640, 12, 0));
        Assert.AreEqual(886, InlineSearchWindowPositioner.CalculatePhysTop(true, hang, dock, 12, 0));
        // 519 + 640 - 12 = 1147, the dialog's own right edge, which is where its list ends.
        Assert.AreEqual(519, InlineSearchWindowPositioner.CalculatePhysLeft(true, false, dock, hang, null, 640, 12, 0));
        Assert.AreEqual(382, InlineSearchWindowPositioner.CalculatePhysTop(false, hang, dock, 12, 0));
    }

    [TestMethod]
    public void APlainWindowKeepsItsRightEdgeDockWhateverTheDialogEdgesSay()
    {
        // Its dock rect already IS its file list, so the adapter is never asked and neither answer can move
        // an Explorer card off the edge it has always used.
        var dock = Box(0, 0, 1000, 800);
        var hang = Hang(dock, Box(10, 700, 990, 790), Box(200, 100, 990, 690));

        Assert.AreEqual(512, InlineSearchWindowPositioner.CalculatePhysLeft(false, false, dock, hang, null, 500, 12, 0));
        Assert.AreEqual(-12, InlineSearchWindowPositioner.CalculatePhysTop(false, Hang(dock), dock, 12, 0));
    }

    [TestMethod]
    public void ADialogThatCannotSeeItsOwnEdgesKeepsThePlacementItAlwaysHad()
    {
        var dock = Box(480, 272, 1440, 920);
        var hang = Hang(dock);

        // Below: centered on the dialog (the WPS 143px fix's fallback), hanging off the dialog's bottom edge.
        Assert.AreEqual(640, InlineSearchWindowPositioner.CalculatePhysLeft(true, true, dock, hang, null, 640, 12, 0));
        Assert.AreEqual(908, InlineSearchWindowPositioner.CalculatePhysTop(true, hang, dock, 12, 0));

        // Over it: the field it feeds still answers horizontally, and its top edge still anchors vertically.
        Assert.AreEqual(777, InlineSearchWindowPositioner.CalculatePhysLeft(
            true, false, dock, hang, Box(783, 799, 1405, 821), 640, 12, 0));
        Assert.AreEqual(260, InlineSearchWindowPositioner.CalculatePhysTop(false, hang, dock, 12, 0));
    }
}
