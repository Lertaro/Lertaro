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
}
