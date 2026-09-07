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

    [TestMethod]
    public void IsDesktopOrShellWindow_ZeroHwnd_ReturnsTrue() =>
        Assert.IsTrue(QuickPanelManager.IsDesktopOrShellWindow(IntPtr.Zero));
}
