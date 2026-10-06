using System.Drawing;
using System.Runtime.InteropServices;

namespace Lertaro.Plugins.WindowSwitcher.Tests;

[TestClass]
public sealed class WindowPositioningTests
{
    [TestMethod]
    [DataRow(2100, 1200, 1908, 1028, 6, 6)]
    [DataRow(2100, 600, 1908, 600, 6, 220)]
    [DataRow(800, 1200, 800, 1028, 560, 6)]
    [DataRow(800, 600, 800, 600, 560, 220)]
    [DataRow(1920, 1040, 1920, 1040, 0, 0)]
    [DataRow(1919, 1039, 1919, 1039, 0, 0)]
    public void CalculateBounds_Fit_OnlyShrinksDimensionsExceedingWorkArea(
        int width, int height, int expectedWidth, int expectedHeight, int expectedX, int expectedY)
    {
        var result = WindowPositioning.CalculateBounds(new Rectangle(700, 500, width, height),
            new Rectangle(0, 0, 1920, 1040), fitToScreen: true);

        Assert.AreEqual(new Rectangle(expectedX, expectedY, expectedWidth, expectedHeight), result);
    }

    [TestMethod]
    [DataRow(false, -1400, -310, 800, 600)]
    [DataRow(true, -1400, -310, 800, 600)]
    public void CalculateBounds_SecondaryMonitorWithTopTaskbar_UsesWorkAreaOrigin(
        bool fit, int x, int y, int width, int height)
    {
        var result = WindowPositioning.CalculateBounds(new Rectangle(300, 400, 800, 600),
            new Rectangle(-1920, -530, 1840, 1040), fit);

        Assert.AreEqual(new Rectangle(x, y, width, height), result);
    }

    [TestMethod]
    public void CalculateBounds_CenterOversizedWindow_PreservesBothDimensions()
    {
        var result = WindowPositioning.CalculateBounds(new Rectangle(50, 80, 2100, 1200),
            new Rectangle(-1920, 40, 1920, 1040), fitToScreen: false);

        Assert.AreEqual(new Rectangle(-2010, -40, 2100, 1200), result);
    }

    [TestMethod]
    public void CalculateBounds_TinyWorkArea_KeepsPositiveDimensions()
    {
        var result = WindowPositioning.CalculateBounds(new Rectangle(0, 0, 50, 50),
            new Rectangle(20, 30, 10, 8), fitToScreen: true);

        Assert.AreEqual(new Rectangle(24, 33, 1, 1), result);
    }

    [TestMethod]
    [DataRow(0, 100, 1920, 1040)]
    [DataRow(100, -1, 1920, 1040)]
    [DataRow(100, 100, 0, 1040)]
    [DataRow(100, 100, 1920, -1)]
    public void CalculateBounds_InvalidDimensions_RejectsInvalidGeometry(int width, int height, int workWidth, int workHeight)
        => Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => WindowPositioning.CalculateBounds(
            new Rectangle(0, 0, width, height), new Rectangle(0, 0, workWidth, workHeight), fitToScreen: true));

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Apply_ClosedWindow_ReturnsFalse(bool fit) => Assert.IsFalse(WindowPositioning.Apply(IntPtr.Zero, fit));

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Apply_NativeWindow_UsesItsMonitorAndPreservesVisibilityAndTopmost(bool fit)
    {
        // An owned, hidden native window exercises the real calls without touching the user's windows.
        // Destroy on this thread in finally: native windows cannot be destroyed by a finalizer thread.
        var hwnd = CreateWindowExW(0x00000008, "STATIC", "Window positioning test", 0x80000000,
            100, 100, 600, 400, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        Assert.AreNotEqual(IntPtr.Zero, hwnd);
        try
        {
            var info = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
            Assert.IsTrue(GetMonitorInfoW(MonitorFromWindow(hwnd, 2), ref info));
            var work = info.WorkArea.ToRectangle();
            // STATIC has no resizing frame. Set a size larger than this display before exercising the menu.
            Assert.IsTrue(SetWindowPos(hwnd, IntPtr.Zero, work.X - 50, work.Y - 50,
                work.Width + 100, work.Height + 100, 0x0414));
            Assert.IsTrue(GetWindowRect(hwnd, out var before));
            Assert.IsTrue(GetMonitorInfoW(MonitorFromWindow(hwnd, 2), ref info));
            work = info.WorkArea.ToRectangle();
            var oldState = WindowMenuOperations.ReadState(hwnd);

            Assert.IsTrue(WindowPositioning.Apply(hwnd, fit));

            Assert.IsTrue(GetWindowRect(hwnd, out var after));
            var actual = after.ToRectangle();
            var original = before.ToRectangle();
            var expectedWidth = fit && original.Width > work.Width ? work.Width - 12 : original.Width;
            var expectedHeight = fit && original.Height > work.Height ? work.Height - 12 : original.Height;
            Assert.AreEqual(new Size(expectedWidth, expectedHeight), actual.Size);
            Assert.AreEqual(work.X + (work.Width - actual.Width) / 2, actual.X);
            Assert.AreEqual(work.Y + (work.Height - actual.Height) / 2, actual.Y);
            Assert.AreEqual(oldState, WindowMenuOperations.ReadState(hwnd));
        }
        finally
        {
            Assert.IsTrue(DestroyWindow(hwnd));
        }
    }

    [TestMethod]
    [DataRow(false, 0x01000000u)]
    [DataRow(true, 0x01000000u)]
    [DataRow(false, 0x20000000u)]
    [DataRow(true, 0x20000000u)]
    public void Apply_MinimizedOrMaximizedWindow_RestoresBeforePositioning(bool fit, uint initialState)
    {
        var hwnd = CreateWindowExW(0, "STATIC", "Window restore test", 0x80000000 | initialState,
            100, 100, 600, 400, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        Assert.AreNotEqual(IntPtr.Zero, hwnd);
        try
        {
            var before = WindowMenuOperations.ReadState(hwnd);
            Assert.IsTrue(before.IsMaximized || before.IsMinimized);
            var info = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
            Assert.IsTrue(GetMonitorInfoW(MonitorFromWindow(hwnd, 2), ref info));
            var work = info.WorkArea.ToRectangle();

            Assert.IsTrue(WindowPositioning.Apply(hwnd, fit));

            var after = WindowMenuOperations.ReadState(hwnd);
            Assert.IsFalse(after.IsMaximized);
            Assert.IsFalse(after.IsMinimized);
            Assert.IsTrue(GetWindowRect(hwnd, out var rect));
            var actual = rect.ToRectangle();
            Assert.AreEqual(new Size(600, 400), actual.Size);
            Assert.AreEqual(work.X + (work.Width - 600) / 2, actual.X);
            Assert.AreEqual(work.Y + (work.Height - 400) / 2, actual.Y);
        }
        finally
        {
            Assert.IsTrue(DestroyWindow(hwnd));
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left, Top, Right, Bottom;
        public readonly Rectangle ToRectangle() => Rectangle.FromLTRB(Left, Top, Right, Bottom);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public uint Size;
        public NativeRect MonitorArea, WorkArea;
        public uint Flags;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern IntPtr CreateWindowExW(uint exStyle, string className, string name, uint style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfoW(IntPtr monitor, ref MonitorInfo info);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
}
