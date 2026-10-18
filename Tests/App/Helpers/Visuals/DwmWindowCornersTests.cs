using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Xml.Linq;
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

    // Telling the window manager about corners needs a handle, and a window has none in its own
    // constructor, which is where every one of this app's windows asks. The request therefore has to
    // survive to the moment the handle appears, or the window stays on the OS default contour, which for
    // a chromeless window is square.
    [StaTestMethod]
    public void CornersAskedForBeforeTheHandleExistsAreAppliedWhenItDoes()
    {
        var asked = ChromelessWindow();
        DwmWindowCorners.ApplyRound(asked);
        var askedPreference = Read(Handle(asked));

        // The control: nobody asked, so this is the contour the window manager chose by itself. Without it
        // the first window could read as rounded simply because the platform rounds everything, and the
        // test would pass while the deferred request did nothing.
        var untouched = ChromelessWindow();
        var untouchedPreference = Read(Handle(untouched));

        if (askedPreference == null || untouchedPreference == null) return; // an OS without the attribute

        Assert.AreEqual(DwmwcpRound, askedPreference, "a request made from the constructor must round the window once the handle exists");
        if (untouchedPreference == DwmwcpRound)
            Assert.Inconclusive("this platform rounds chromeless windows by default, so nothing here can tell an applied request from none");
        Assert.AreNotEqual(untouchedPreference, askedPreference, "the request has to be what changed the contour");
    }

    // The painted contour is a second curve drawn inside the one the window manager cuts, and it only
    // reads as one shape when the two agree. They drifted for a release cycle here: the token said 10 and
    // two LocalSend windows said 12 in markup that never referenced the token, while DWM cuts 8, so the
    // 1 DIP window border could not follow the cut and its arcs broke off at every corner.
    [TestMethod]
    public void PaintedWindowRadiusSitsOnTheWindowManagerContour()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AGENTS.md"))) dir = dir.Parent;
        Assert.IsNotNull(dir, "could not locate the repository root");
        var tokens = XDocument.Load(Path.Combine(dir!.FullName, "App/Resources/Styles/Tokens/Spacing.xaml"));
        // The key is markup-namespaced (x:Key): a plain Attribute("Key") matches nothing, so every line
        // below would report a missing token instead of the wrong radius it is actually guarding.
        var xaml = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
        string Radius(string key) => tokens.Descendants().Single(e => (string?)e.Attribute(xaml + "Key") == key).Value.Trim();

        Assert.AreEqual("8", Radius("CornerRadiusWindow"), "DWMWCP_ROUND cuts a fixed 8 DIP, the contour WindowSurface and PluginWindow already paint");
        // Header and footer bands sit one BorderThickness inside the outer curve, per the token comment.
        Assert.AreEqual("7,7,0,0", Radius("CornerRadiusWindowTop"));
        Assert.AreEqual("0,0,7,7", Radius("CornerRadiusWindowBottom"));
    }

    // Creating the handle is what Show() does internally and what the deferred request waits for, so this
    // never puts a window on the screen of whoever is running the suite. The handles go with the process.
    private static IntPtr Handle(Window window) => new WindowInteropHelper(window).EnsureHandle();

    private static Window ChromelessWindow() => new()
    {
        WindowStyle = WindowStyle.None,
        AllowsTransparency = false,
        ShowInTaskbar = false,
        Width = 40,
        Height = 20
    };

    private static int? Read(IntPtr hwnd) =>
        DwmGetWindowAttribute(hwnd, DwmwaWindowCornerPreference, out var preference, sizeof(int)) >= 0
            ? preference
            : null;
}
