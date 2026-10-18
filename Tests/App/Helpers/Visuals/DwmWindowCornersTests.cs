using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Lertaro.App.Helpers.Visuals;

namespace Lertaro.App.Tests.Helpers.Visuals;

[TestClass]
[DoNotParallelize]
public sealed class DwmWindowCornersTests
{
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwcpRound = 2;

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);

    // Telling the window manager about corners needs a handle, and a window has none yet in its own
    // constructor, which is where every one of this app's windows asks. The request therefore has to
    // survive to the moment the handle appears, or the window stays on the OS default contour, which for
    // a chromeless window is square.
    [StaTestMethod]
    public void CornersAskedForBeforeTheHandleExistsAreAppliedWhenItDoes()
    {
        var early = ParkedWindow();
        DwmWindowCorners.ApplyRound(early);
        early.Show();
        var askedFromTheConstructor = ReadCorners(early);
        early.Close();

        var direct = ParkedWindow();
        direct.Show();
        DwmWindowCorners.ApplyRound(direct);
        var askedAfterTheHandle = ReadCorners(direct);
        direct.Close();

        if (askedAfterTheHandle == null) return; // an OS without the attribute: there is no contour to compare
        Assert.AreEqual(DwmwcpRound, askedAfterTheHandle, "asking once the handle exists must round the window");
        Assert.AreEqual(askedAfterTheHandle, askedFromTheConstructor, "asking from the constructor must end on the same contour");
    }

    // -32000 on both axes: the same spot the app parks windows it never wants looked at, so the run does
    // not flash a window in front of whoever is working.
    private static Window ParkedWindow() => new()
    {
        WindowStyle = WindowStyle.None,
        AllowsTransparency = false,
        ShowInTaskbar = false,
        Width = 40,
        Height = 20,
        Left = -32000,
        Top = -32000
    };

    private static int? ReadCorners(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        return DwmGetWindowAttribute(hwnd, DwmwaWindowCornerPreference, out var preference, sizeof(int)) >= 0
            ? preference
            : null;
    }
}
