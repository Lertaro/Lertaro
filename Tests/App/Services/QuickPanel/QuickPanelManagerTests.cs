using Lertaro.App.Services.QuickPanel;

namespace Lertaro.App.Tests.Services.QuickPanel;

[TestClass]
public sealed class QuickPanelManagerTests
{
    [TestMethod]
    public void IsCurrentProcess_MatchesTheForegroundApp() => Assert.IsTrue(QuickPanelManager.IsCurrentProcess(42, 42));

    [TestMethod]
    public void IsCurrentProcess_RejectsOtherAndZeroProcesses()
    {
        Assert.IsFalse(QuickPanelManager.IsCurrentProcess(7, 42));
        Assert.IsFalse(QuickPanelManager.IsCurrentProcess(0, 0));
    }

    [TestMethod]
    public void CalculateDockPosition_SameDpi_DocksToBottomRight()
    {
        // Host window at (100, 100, 900, 700) with 96 DPI (1.0x scale)
        // Host width = 800, height = 600
        // Panel width = 800 * 0.5 = 400, panel height = 600 * 0.5 = 300
        // Target physical: Left = 900 - 400 - 12 = 488, Top = 700 - 300 - 12 = 388
        var (width, height, left, top) = QuickPanelManager.CalculateDockPosition(
            hostLeft: 100, hostTop: 100, hostRight: 900, hostBottom: 700,
            hostDpi: 96,
            waLeft: 0, waTop: 0, waWidth: 1920, waHeight: 1080,
            currentDpiScaleX: 1.0, currentDpiScaleY: 1.0);

        Assert.AreEqual(400.0, width, 0.001);
        Assert.AreEqual(300.0, height, 0.001);
        Assert.AreEqual(488.0, left, 0.001);
        Assert.AreEqual(388.0, top, 0.001);
    }

    [TestMethod]
    public void CalculateDockPosition_MixedDpi_CompensatesCurrentWindowScale()
    {
        // Host window on 125% DPI display (120 DPI), while newly instantiated panel has 150% DPI (1.5x)
        // Host physical bounds: (3840, 0, 4840, 800) -> physical width 1000px, physical height 800px
        // Host DIP size: width = 1000 / 1.25 = 800 DIP, height = 800 / 1.25 = 640 DIP
        // Panel DIP size: width = 400 DIP, height = 320 DIP
        // Panel physical size: 400 * 1.25 = 500px, 320 * 1.25 = 400px
        // Target physical: Left = 4840 - 500 - (12 * 1.25) = 4325px, Top = 800 - 400 - (12 * 1.25) = 385px
        // Compensated for WPF 1.5x scale: Left = 4325 / 1.5, Top = 385 / 1.5
        var (width, height, left, top) = QuickPanelManager.CalculateDockPosition(
            hostLeft: 3840, hostTop: 0, hostRight: 4840, hostBottom: 800,
            hostDpi: 120,
            waLeft: 3840, waTop: 0, waWidth: 1920, waHeight: 1080,
            currentDpiScaleX: 1.5, currentDpiScaleY: 1.5);

        Assert.AreEqual(400.0, width, 0.001);
        Assert.AreEqual(320.0, height, 0.001);
        Assert.AreEqual(4325.0 / 1.5, left, 0.001);
        Assert.AreEqual(385.0 / 1.5, top, 0.001);

        // When WPF scales Left and Top by 1.5x on the HWND, it hits exact physical pixels:
        Assert.AreEqual(4325.0, left * 1.5, 0.001);
        Assert.AreEqual(385.0, top * 1.5, 0.001);
    }

    // A size the user dragged the panel to outlives the summon: the automatic figure is only ever a
    // stand-in for someone who has not chosen one.
    [TestMethod]
    public void CalculatePhysicalDockPosition_AUserSize_ReplacesTheAutomaticOne()
    {
        var (width, height, _, _) = QuickPanelManager.CalculatePhysicalDockPosition(
            hostLeft: 0, hostTop: 0, hostRight: 1000, hostBottom: 900,
            hostDpi: 96,
            waLeft: 0, waTop: 0, waWidth: 1920, waHeight: 1080,
            userWidthDip: 640, userHeightDip: 300);

        Assert.AreEqual(640.0, width, 0.001, "the user's width, not half the host");
        Assert.AreEqual(300.0, height, 0.001);
    }

    // And it is NOT held to the automatic cap: that cap exists to stop the panel growing with a maximized
    // host, and a size the user chose by hand is not the panel growing on its own.
    [TestMethod]
    public void CalculatePhysicalDockPosition_AUserSize_IsNotHeldToTheAutoCap()
    {
        var (width, height, _, _) = QuickPanelManager.CalculatePhysicalDockPosition(
            hostLeft: 0, hostTop: 0, hostRight: 3840, hostBottom: 2160,
            hostDpi: 96,
            waLeft: 0, waTop: 0, waWidth: 3840, waHeight: 2160,
            userWidthDip: 900, userHeightDip: 700);

        Assert.IsGreaterThan(QuickPanelManager.MaxAutoWidth, width, "a hand-picked size may exceed the auto cap");
        Assert.IsGreaterThan(QuickPanelManager.MaxAutoHeight, height);
    }

    // A user size still gets docked to the host's bottom-right rather than opening off the edge of it:
    // the size is the panel's own, the position belongs to the host.
    [TestMethod]
    public void CalculatePhysicalDockPosition_AUserSize_IsStillDockedInsideTheHost()
    {
        var (width, height, physLeft, physTop) = QuickPanelManager.CalculatePhysicalDockPosition(
            hostLeft: 100, hostTop: 100, hostRight: 900, hostBottom: 700,
            hostDpi: 96,
            waLeft: 0, waTop: 0, waWidth: 1920, waHeight: 1080,
            userWidthDip: 400, userHeightDip: 300);

        Assert.AreEqual(400.0, width, 0.001);
        Assert.AreEqual(300.0, height, 0.001);
        Assert.AreEqual(488.0, physLeft, 0.001, "the same bottom-right corner, less the margin");
        Assert.AreEqual(388.0, physTop, 0.001);
    }

    // The floor still wins over a user size, so a settings file holding something absurd cannot produce a
    // window too small to show anything.
    [TestMethod]
    public void CalculatePhysicalDockPosition_ATinyUserSize_IsRaisedToTheMinimum()
    {
        var (width, height, _, _) = QuickPanelManager.CalculatePhysicalDockPosition(
            hostLeft: 0, hostTop: 0, hostRight: 1000, hostBottom: 900,
            hostDpi: 96,
            waLeft: 0, waTop: 0, waWidth: 1920, waHeight: 1080,
            userWidthDip: 20, userHeightDip: 20);

        Assert.AreEqual(280.0, width, 0.001);
        Assert.AreEqual(200.0, height, 0.001);
    }

    [TestMethod]
    public void CalculatePhysicalDockPosition_SameDpi_ComputesCorrectPhysicalCoordinates()
    {
        var (width, height, physLeft, physTop) = QuickPanelManager.CalculatePhysicalDockPosition(
            hostLeft: 100, hostTop: 100, hostRight: 900, hostBottom: 700,
            hostDpi: 96,
            waLeft: 0, waTop: 0, waWidth: 1920, waHeight: 1080);

        Assert.AreEqual(400.0, width, 0.001);
        Assert.AreEqual(300.0, height, 0.001);
        Assert.AreEqual(488.0, physLeft, 0.001);
        Assert.AreEqual(388.0, physTop, 0.001);
    }

    // Half of a maximized 4K window is a panel with more room than a screenful of files can fill, so the
    // automatic size stops at the cap. A manual drag of the grip is not this path and is not capped.
    [TestMethod]
    public void CalculatePhysicalDockPosition_AVeryLargeHost_StopsAtTheAutomaticCap()
    {
        var (width, height, _, _) = QuickPanelManager.CalculatePhysicalDockPosition(
            hostLeft: 0, hostTop: 0, hostRight: 3840, hostBottom: 2160,
            hostDpi: 96,
            waLeft: 0, waTop: 0, waWidth: 3840, waHeight: 2160);

        Assert.AreEqual(QuickPanelManager.MaxAutoWidth, width, 0.001);
        Assert.AreEqual(QuickPanelManager.MaxAutoHeight, height, 0.001);
    }

    // The floor still wins where it did: a small host gets a panel no smaller than the minimum, so the
    // cap cannot be mistaken for a clamp that squeezes both ends.
    [TestMethod]
    public void CalculatePhysicalDockPosition_ATinyHost_StillGetsTheMinimum()
    {
        var (width, height, _, _) = QuickPanelManager.CalculatePhysicalDockPosition(
            hostLeft: 0, hostTop: 0, hostRight: 120, hostBottom: 120,
            hostDpi: 96,
            waLeft: 0, waTop: 0, waWidth: 1920, waHeight: 1080);

        Assert.AreEqual(280.0, width, 0.001);
        Assert.AreEqual(200.0, height, 0.001);
    }

    // In between the two ends, the host still decides: the cap is a ceiling, not the size.
    [TestMethod]
    public void CalculatePhysicalDockPosition_AnOrdinaryHost_IsStillHalfOfIt()
    {
        var (width, height, _, _) = QuickPanelManager.CalculatePhysicalDockPosition(
            hostLeft: 0, hostTop: 0, hostRight: 1000, hostBottom: 900,
            hostDpi: 96,
            waLeft: 0, waTop: 0, waWidth: 1920, waHeight: 1080);

        Assert.AreEqual(500.0, width, 0.001);
        Assert.AreEqual(450.0, height, 0.001);
    }

    [TestMethod]
    public void IsDesktopOrShellWindow_ZeroHwnd_ReturnsTrue() =>
        Assert.IsTrue(QuickPanelManager.IsDesktopOrShellWindow(IntPtr.Zero));
}
