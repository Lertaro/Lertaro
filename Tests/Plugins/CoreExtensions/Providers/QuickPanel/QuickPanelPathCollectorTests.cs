using Lertaro.Plugins.CoreExtensions.Providers.QuickPanel;

namespace Lertaro.Plugins.CoreExtensions.Tests.Providers.QuickPanel;

[TestClass]
public sealed class QuickPanelPathCollectorTests
{
    [TestMethod]
    public void IsQuickPanelWindow_LertaroQuickPanel_ReturnsTrue() =>
        Assert.IsTrue(QuickPanelPathCollector.IsQuickPanelWindow("HwndWrapper[Lertaro.App;;abc]", "Lertaro Quick Panel"));

    [TestMethod]
    public void IsQuickPanelWindow_OtherLertaroWindow_ReturnsFalse() =>
        Assert.IsFalse(QuickPanelPathCollector.IsQuickPanelWindow("HwndWrapper[Lertaro.App;;abc]", "Lertaro Settings"));

    [TestMethod]
    public void IsQuickPanelWindow_NonWpfWindow_ReturnsFalse() =>
        Assert.IsFalse(QuickPanelPathCollector.IsQuickPanelWindow("CabinetWClass", "Lertaro Quick Panel"));

    [TestMethod]
    public void CanHandle_ClassAndTitle_RecognizesQuickPanel()
    {
        var collector = new QuickPanelPathCollector();

        Assert.IsTrue(collector.CanHandle("HwndWrapper[Lertaro.App;;abc]", "Lertaro Quick Panel"));
    }
}
