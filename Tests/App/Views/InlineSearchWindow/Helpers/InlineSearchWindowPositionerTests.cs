using Lertaro.App.Views.InlineSearchWindow.Helpers;

namespace Lertaro.App.Tests.Views.InlineSearchWindow.Helpers;

[TestClass]
public sealed class InlineSearchWindowPositionerTests
{
    [TestMethod]
    public void CalculateDockedWidth_UsesHalfOfTargetWindow() => Assert.AreEqual(600, InlineSearchWindowPositioner.CalculateDockedWidth(1200));

    [TestMethod]
    public void CalculateDockedWidth_DoesNotApplyMinimumWidth() => Assert.AreEqual(400, InlineSearchWindowPositioner.CalculateDockedWidth(800));

    [TestMethod]
    public void CalculateDesktopWidth_UsesTwentyPercentOfWorkingArea() => Assert.AreEqual(384, InlineSearchWindowPositioner.CalculateDesktopWidth(1920));
}
