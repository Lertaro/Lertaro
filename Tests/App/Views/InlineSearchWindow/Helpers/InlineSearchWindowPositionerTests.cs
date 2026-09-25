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

    // The tests below use two real dialogs, measured off live windows with Lertaro's own probes at 96 DPI
    // (so physical px == DIP):
    //
    //   Firefox's 另存为  -- #32770, GetWindowRect 184,314..1055,931, DWM frame 191,314..1048,924, its file
    //                        list SHELLDLL_DefView at 352,438..1047,788.
    //   WPS's 上传到云     -- 187,310..1147,958, file list KcfdContentWidget at 396,394..1147,818.
    //
    // Card width is the existing 2/3 of the dock rect: 571 for the Firefox dialog (857 wide), 640 for WPS.
    private static Core.Hook.ExplorerTracker.RECT Box(int left, int top, int right, int bottom) =>
        new() { Left = left, Top = top, Right = right, Bottom = bottom };

    [TestMethod]
    public void ACardWithRoomBelowIsCenteredUnderTheWindowItHangsFrom()
    {
        var dock = Box(191, 314, 1048, 924);
        var fileList = Box(352, 438, 1047, 788);

        var left = InlineSearchWindowPositioner.CalculatePhysLeft(true, true, dock, fileList, null, 571, 12, 0);
        var top = InlineSearchWindowPositioner.CalculatePhysTop(true, dock, fileList, 12, 0);

        Assert.AreEqual(912, top, "its visible top edge meets the dialog's bottom edge");
        Assert.AreEqual(334, left);
        Assert.AreEqual((191 + 1048) / 2.0, left + 571 / 2.0, 0.5, "and the card is centered on the dialog");
    }

    [TestMethod]
    public void ADialogCardWithNoRoomBelowHangsOffItsFileListsTopRightCorner()
    {
        var dock = Box(191, 314, 1048, 924);
        var fileList = Box(352, 438, 1047, 788);

        var left = InlineSearchWindowPositioner.CalculatePhysLeft(true, false, dock, fileList, null, 571, 12, 0);
        var top = InlineSearchWindowPositioner.CalculatePhysTop(false, dock, fileList, 12, 0);

        // Visible right edge = 488 + 571 - 12 = 1047 and visible top = 426 + 12 = 438, i.e. the card's top
        // right corner on the list's, hanging down over it -- so it covers files, which scroll, rather than
        // the address bar and the navigation pane.
        Assert.AreEqual(488, left);
        Assert.AreEqual(426, top);
    }

    [TestMethod]
    public void TheSameTwoRulesComeOutOnAWpsDialog()
    {
        var dock = Box(187, 310, 1147, 958);
        var fileList = Box(396, 394, 1147, 818);

        Assert.AreEqual(347, InlineSearchWindowPositioner.CalculatePhysLeft(true, true, dock, fileList, null, 640, 12, 0));
        Assert.AreEqual(946, InlineSearchWindowPositioner.CalculatePhysTop(true, dock, fileList, 12, 0));
        // 519 + 640 - 12 = 1147: the dialog's own right edge, which is also where its list ends.
        Assert.AreEqual(519, InlineSearchWindowPositioner.CalculatePhysLeft(true, false, dock, fileList, null, 640, 12, 0));
        Assert.AreEqual(382, InlineSearchWindowPositioner.CalculatePhysTop(false, dock, fileList, 12, 0));
    }

    [TestMethod]
    public void APlainWindowCentersBelowAndKeepsItsRightEdgeDockOver()
    {
        // Its dock rect already IS its file list, so no adapter answer is involved in either placement, and
        // the same two rules apply to it as to any dialog -- which is what makes a card over Total Commander
        // and a card over a Save dialog behave alike.
        var dock = Box(0, 0, 1000, 800);

        Assert.AreEqual(250, InlineSearchWindowPositioner.CalculatePhysLeft(false, true, dock, null, null, 500, 12, 0));
        Assert.AreEqual(788, InlineSearchWindowPositioner.CalculatePhysTop(true, dock, null, 12, 0));
        Assert.AreEqual(512, InlineSearchWindowPositioner.CalculatePhysLeft(false, false, dock, null, null, 500, 12, 0));
        Assert.AreEqual(-12, InlineSearchWindowPositioner.CalculatePhysTop(false, dock, null, 12, 0));
    }

    [TestMethod]
    public void ADialogThatCannotSeeItsOwnFileListKeepsThePlacementItAlwaysHad()
    {
        var dock = Box(480, 272, 1440, 920);

        // Below: centered, hanging off the dialog's bottom edge -- which the rule above does anyway, so the
        // dialogs that cannot answer lose nothing.
        Assert.AreEqual(640, InlineSearchWindowPositioner.CalculatePhysLeft(true, true, dock, null, null, 640, 12, 0));
        Assert.AreEqual(908, InlineSearchWindowPositioner.CalculatePhysTop(true, dock, null, 12, 0));

        // Over it: the field it feeds still answers horizontally, and its top edge still anchors vertically.
        Assert.AreEqual(777, InlineSearchWindowPositioner.CalculatePhysLeft(
            true, false, dock, null, Box(783, 799, 1405, 821), 640, 12, 0));
        Assert.AreEqual(260, InlineSearchWindowPositioner.CalculatePhysTop(false, dock, null, 12, 0));
    }
}
