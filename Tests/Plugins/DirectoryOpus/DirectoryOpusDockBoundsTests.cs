using System.Runtime.InteropServices;
using Lertaro.Plugins.DirectoryOpus.Win32;

namespace Lertaro.Plugins.DirectoryOpus.Tests;

// Where the card docks inside a Directory Opus lister is decided by which of its windows the adapter names,
// and a lister is stacked: frame 196, client area 227 (the system title bar in between, with the lister's own
// close button at its right end), two toolbar rows, each pane's path row, and only then the file display at
// 331. The numbers below are those bands, built as real windows in this process because the adapter reads them
// back from Win32 rather than from anything a test could hand it.
[TestClass]
public sealed class DirectoryOpusDockBoundsTests
{
    private static readonly IntPtr DefWindowProc = GetProcAddress(GetModuleHandle("user32.dll"), "DefWindowProcW");

    private const int WsPopup = unchecked((int)0x80000000);
    private const int WsChild = 0x40000000;
    private const int WsVisible = 0x10000000;

    [TestMethod]
    public void TheCardDocksToTheFileDisplaysAndNotToThePanesOrTheWindow()
    {
        using var lister = new FakeLister(withFileDisplays: true);

        var adapter = new DirectoryOpusInlineSearchAdapter();
        Assert.IsTrue(adapter.GetDockBounds(lister.Hwnd, out var dock), "the lister has file displays to name");

        Assert.AreEqual(lister.FileDisplayA.Left, dock.Left, "the left edge is the left pane's file list");
        Assert.AreEqual(lister.FileDisplayA.Top, dock.Top, "and its top is the file list's, not its container's");
        Assert.AreEqual(lister.FileDisplayB.Right, dock.Right, "the right edge is the right pane's file list");
        Assert.AreEqual(Math.Max(lister.FileDisplayA.Bottom, lister.FileDisplayB.Bottom), dock.Bottom);

        // The hidden icon view is the one that would have stretched this: it starts higher than the file
        // displays. Reading it back is what makes the assertion above a check of the visibility filter rather
        // than a tautology.
        Assert.IsTrue(ListerRect(lister.HiddenIconHandle).Top < dock.Top,
            "the hidden icon display starts above the dock, so it was left out on purpose");
        Assert.IsTrue(lister.ContainerATop < dock.Top,
            "the container, and every band above it, starts above the file display");
    }

    [TestMethod]
    public void AFileDisplayHandleIsAnsweredForTheListerItBelongsTo()
    {
        // The hook hands the focused control to the adapter, so the walk up to the lister has to happen before
        // the panes are collected -- otherwise a dual-display lister would dock to one pane alone.
        using var lister = new FakeLister(withFileDisplays: true);

        var adapter = new DirectoryOpusInlineSearchAdapter();
        Assert.IsTrue(adapter.GetDockBounds(lister.FileDisplayHandle, out var fromPane));
        Assert.IsTrue(adapter.GetDockBounds(lister.Hwnd, out var fromLister));

        Assert.AreEqual(fromLister.Left, fromPane.Left);
        Assert.AreEqual(fromLister.Top, fromPane.Top);
        Assert.AreEqual(fromLister.Right, fromPane.Right);
        Assert.AreEqual(fromLister.Bottom, fromPane.Bottom);
    }

    [TestMethod]
    public void AListerShowingNoFileDisplayStillDocksToItsOwnFrame()
    {
        using var lister = new FakeLister(withFileDisplays: false);

        var adapter = new DirectoryOpusInlineSearchAdapter();
        Assert.IsTrue(adapter.GetDockBounds(lister.Hwnd, out var dock), "the window itself is always an answer");
        Assert.IsTrue(Win32Helper.TryGetWindowRect(lister.Hwnd, out var frame));

        // The frame top, not a band below it: the App then keeps the card clear of the title bar with its own
        // client-area floor (InlineSearchWindowPositioner.CalculatePhysTop).
        Assert.IsTrue(dock.Top <= frame.Top + 5, $"dock top {dock.Top} should be the frame's own {frame.Top}");
    }

    private static Win32Helper.RECT ListerRect(IntPtr hwnd)
    {
        Assert.IsTrue(Win32Helper.TryGetWindowRect(hwnd, out var r), $"0x{hwnd:x} has no rect to read");
        return r;
    }

    // One lister, two panes, one file display inside each, and a hidden icon view that overlaps them all.
    private sealed class FakeLister : IDisposable
    {
        public IntPtr Hwnd { get; }
        public IntPtr ContainerA { get; private set; }
        public IntPtr FileDisplayHandle { get; private set; }
        public IntPtr HiddenIconHandle { get; private set; }

        public Win32Helper.RECT FileDisplayA => ListerRect(FileDisplayHandle);
        public Win32Helper.RECT FileDisplayB => ListerRect(_fileDisplayB);
        public int ContainerATop => ListerRect(ContainerA).Top;

        private IntPtr _fileDisplayB;

        public FakeLister(bool withFileDisplays)
        {
            Register("dopus.lister");
            Register("dopus.filedisplaycontainer");
            Register("dopus.filedisplay");
            Register("dopus.iconfiledisplay");

            // Visible, because IsWindowVisible answers false for a child of a hidden parent and the whole
            // point is which children count -- and parked far off screen so nothing flashes across the desktop.
            Hwnd = Create("dopus.lister", IntPtr.Zero, WsPopup | WsVisible, -30000, -30000, 600, 400);

            if (!withFileDisplays) return;

            // The bands as a live lister stacks them: the pane's own path row is the container's top 30px,
            // and the file list starts below it.
            ContainerA = Create("dopus.filedisplaycontainer", Hwnd, WsChild | WsVisible, 10, 60, 300, 250);
            var containerB = Create("dopus.filedisplaycontainer", Hwnd, WsChild | WsVisible, 310, 60, 290, 250);
            FileDisplayHandle = Create("dopus.filedisplay", ContainerA, WsChild | WsVisible, 1, 30, 290, 200);
            _fileDisplayB = Create("dopus.filedisplay", containerB, WsChild | WsVisible, 1, 30, 280, 200);
            // The view mode this pane is not in: a bigger footprint than both file lists, but hidden.
            HiddenIconHandle = Create("dopus.iconfiledisplay", containerB, WsChild, -10, -20, 590, 400);
        }

        public void Dispose()
        {
            if (Hwnd != IntPtr.Zero) DestroyWindow(Hwnd);
        }

        private static IntPtr Create(string className, IntPtr parent, int style, int x, int y, int w, int h)
        {
            var hwnd = CreateWindowEx(0, className, null, style, x, y, w, h, parent, IntPtr.Zero,
                GetModuleHandle(null), IntPtr.Zero);
            Assert.AreNotEqual(IntPtr.Zero, hwnd, $"{className} could not be created");
            return hwnd;
        }

        private static void Register(string className)
        {
            var wc = new WndClassEx
            {
                cbSize = (uint)Marshal.SizeOf<WndClassEx>(),
                lpfnWndProc = DefWindowProc,
                hInstance = GetModuleHandle(null),
                lpszClassName = className,
            };
            // Fails for a class this process already has, which is the normal case across a test run; the
            // windows are created by name either way.
            RegisterClassEx(ref wc);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WndClassEx
    {
        public uint cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
        public IntPtr hIconSm;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassEx(ref WndClassEx lpwcx);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(int dwExStyle, string lpClassName, string? lpWindowName,
        int dwStyle, int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu,
        IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi)]
    private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);
}
