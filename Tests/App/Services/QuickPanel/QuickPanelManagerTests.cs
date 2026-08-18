using Lertaro.App.Services.QuickPanel;

namespace Lertaro.App.Tests.Services.QuickPanel;

[TestClass]
public sealed class QuickPanelManagerTests
{
    [TestMethod]
    public void IsCurrentProcess_MatchesTheForegroundApp()
    {
        Assert.IsTrue(QuickPanelManager.IsCurrentProcess(42, 42));
    }

    [TestMethod]
    public void IsCurrentProcess_RejectsOtherAndZeroProcesses()
    {
        Assert.IsFalse(QuickPanelManager.IsCurrentProcess(7, 42));
        Assert.IsFalse(QuickPanelManager.IsCurrentProcess(0, 0));
    }
}
