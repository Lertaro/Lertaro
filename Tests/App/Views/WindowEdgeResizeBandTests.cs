using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using System.Xml.Linq;

namespace Lertaro.App.Tests.Views;

// Two attributes hold this together, and both fail silently: the chrome resize band is what used to swallow the
// pointer over the 6px overlay scrollbar pressed against every window edge, and the grip only resizes because
// its style declares the direction -- measured with WM_NCHITTEST against a window parked off screen, a
// ResizeGrip left at None answers HTCLIENT once the band is gone. A later edit to either one costs the user
// either the scrollbar or the ability to resize the window, and nothing about either mistake shows up in a
// screenshot.
//
// [DoNotParallelize] because the third case shows and closes a real window on its own dispatcher thread.
[TestClass]
[DoNotParallelize]
public sealed class WindowEdgeResizeBandTests
{
    private const int HTClient = 1;
    private const int HTBottomRight = 17;

    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [TestMethod]
    public void NoWindowDeclaresAChromeResizeBand()
    {
        var declarations = 0;
        foreach (var path in Directory.EnumerateFiles(Path.Combine(RepositoryRoot(), "App"), "*.xaml", SearchOption.AllDirectories)
                     .Where(p => !p.Split(Path.DirectorySeparatorChar).Any(s => s is "obj" or "bin")))
        {
            foreach (var chrome in XDocument.Load(path).Descendants(Presentation + "WindowChrome"))
            {
                declarations++;
                Assert.AreEqual("0", (string?)chrome.Attribute("ResizeBorderThickness"),
                    $"{Path.GetRelativePath(RepositoryRoot(), path)} claims a resize band, which covers the"
                    + " overlay scrollbar along that edge and makes it impossible to drag");
            }
        }

        Assert.IsGreaterThanOrEqualTo(7, declarations, "every window with custom chrome should have been read");
    }

    [TestMethod]
    public void ResizeGripStyle_IsTheThingThatResizes()
    {
        var path = Path.Combine(RepositoryRoot(), "App", "Resources", "Styles", "Controls", "ResizeGrip.xaml");
        var xaml = XDocument.Load(path);

        var direction = xaml.Descendants(Presentation + "Setter")
            .FirstOrDefault(s => (string?)s.Attribute("Property") == "shell:WindowChrome.ResizeGripDirection");
        Assert.IsNotNull(direction, "without the direction the grip is inert, and with no resize band nothing resizes at all");
        Assert.AreEqual("BottomRight", (string?)direction!.Attribute("Value"));

        // Strokes alone are not a target: InputHitTest only answers where the Path is drawn, so the grip needs a
        // filled root to make its whole box grabbable.
        var root = xaml.Descendants(Presentation + "ControlTemplate").Single().Elements().FirstOrDefault();
        Assert.AreEqual("Grid", root?.Name.LocalName, "the grip template needs a root that covers the whole grip box");
        Assert.AreEqual("Transparent", (string?)root?.Attribute("Background"));
    }

    [StaTestMethod]
    public void ShippedGripStyle_StillResizesWithNoBandAtAll()
    {
        // The grip element is created by Window, not by any of ours, so this is the only way to ask whether the
        // shipped style actually reaches it. The window is parked off screen at the row WindowParking uses and
        // is never brought back, so nothing appears while this runs.
        var window = new Window
        {
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.CanResizeWithGrip,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -32000,
            Top = -32000,
            Width = 300,
            Height = 200,
            Content = new Grid { Background = Brushes.White },
        };
        WindowChrome.SetWindowChrome(window, new WindowChrome
        {
            CaptionHeight = 0,
            ResizeBorderThickness = new Thickness(0),
            GlassFrameThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            UseAeroCaptionButtons = false,
        });
        window.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/Lertaro.App;component/Resources/Styles/Controls/ResizeGrip.xaml", UriKind.Absolute),
        });

        try
        {
            window.Show();
            PumpAndLayout(window);

            var grip = Find<ResizeGrip>(window);
            Assert.IsNotNull(grip, "ResizeMode=CanResizeWithGrip should have created the grip element");
            Assert.AreEqual(ResizeGripDirection.BottomRight, WindowChrome.GetResizeGripDirection(grip!),
                "the shipped style has to carry the direction onto the element Window creates");

            var hwnd = new WindowInteropHelper(window).Handle;
            Assert.IsTrue(GetWindowRect(hwnd, out var rect));
            var centre = grip!.PointToScreen(new Point(grip.ActualWidth / 2, grip.ActualHeight / 2));
            Assert.AreEqual(HTBottomRight, Hit(hwnd, centre), "the grip alone must still answer the corner, with no band behind it");
            Assert.AreEqual(HTClient, Hit(hwnd, new Point(rect.Right - 2, (rect.Top + rect.Bottom) / 2)),
                "and the window's right edge has to hand the pointer back to the content there, which is where the scrollbar sits");
        }
        finally
        {
            window.Close();
        }
    }

    private static void PumpAndLayout(Window window)
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Loaded, new DispatcherOperationCallback(_ =>
        {
            frame.Continue = false;
            return null;
        }), Array.Empty<object>());
        Dispatcher.PushFrame(frame);
        window.UpdateLayout();
    }

    private static int Hit(IntPtr hwnd, Point screen)
        => (int)SendMessageW(hwnd, 0x0084 /* WM_NCHITTEST */, IntPtr.Zero, (IntPtr)(((int)screen.Y << 16) | ((int)screen.X & 0xFFFF))).ToInt64();

    private static T? Find<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) return match;
            if (Find<T>(child) is { } deeper) return deeper;
        }
        return null;
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AGENTS.md")))
            dir = dir.Parent;
        Assert.IsNotNull(dir, "could not locate the repository root");
        return dir.FullName;
    }
}
