using System.Runtime.InteropServices;
using System.Text;

namespace Lertaro.Plugins.Xplorer2.Win32;

/// <summary>
/// Passive reads of a live xplorer2 window's own structure: classes, control text, geometry. No command
/// numbers and no navigation here -- that is Win32Helper.
///
/// Structural facts, measured against a live 6.4.0.0 install (two windows, several tabs each):
///   - Frame class is "ATL:ExplorerFrame"; scrap/dir-stat/cmd windows use different classes
///     (ATL:ScrapFrame, ATL:DirStatFrame, ATL:CmdFrame).
///   - Folder panes are SysListView32 under WTL_Miller under WTL_PaneContainer2; the mini-scrap bin is a
///     SysListView32 too but sits under WTL_DockingChildWindow instead, so the class alone is ambiguous.
///   - The address bar's ComboBox answers WM_GETTEXT with the active pane's real, full folder path
///     ("D:\Notes") while the window title shows an elided form ("Notes @ D:\"). Only the send
///     channel carries it: GetWindowText on the same HWND comes back empty.
///   - ATL names its window classes from the format string "ATL:%p", so a class like
///     "ATL:00000000004A5D60" embeds a live address and differs between runs of the same process. Match
///     those by their accessible text ("CrumbBar"), never by class.
/// </summary>
public static class WindowProbe
{
    public const string FrameClass = "ATL:ExplorerFrame";
    public const string FileListClass = "SysListView32";
    public const string MillerClass = "WTL_Miller";
    public const string CrumbBarText = "CrumbBar";

    // The frame's own content host: everything below the ribbon and the toolbar band, panes and docked
    // side panels alike. Its right edge does not move when a side panel is shown or hidden, which is what
    // makes it usable as the single anchor the card hangs from.
    private const string DockContentClass = "WTL_DockingWindow";

    private const uint WM_GETTEXT = 0x000D;
    private const uint WM_SETTEXT = 0x000C;
    private const uint SMTO_ABORTIFHUNG = 0x0002;
    private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

    private delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc callback, IntPtr parameter);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageTimeoutW")]
    private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam,
        uint flags, uint timeout, out IntPtr result);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO lpgui);

    [DllImport("user32.dll")]
    private static extern IntPtr GetParent(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GUITHREADINFO
    {
        public int cbSize;
        public uint flags;
        public IntPtr hwndActive;
        public IntPtr hwndFocus;
        public IntPtr hwndCapture;
        public IntPtr hwndMenuOwner;
        public IntPtr hwndMoveSize;
        public IntPtr hwndCaret;
        public RECT rcCaret;
    }

    public static string GetClassName(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero) return string.Empty;
        var sb = new StringBuilder(512);
        GetClassNameW(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }

    /// <summary>
    /// Reads a control's text over WM_GETTEXT. user32 marshals this message, so the buffer stays in our
    /// own address space -- a VirtualAllocEx pointer would read back empty.
    /// </summary>
    public static string GetText(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero) return string.Empty;
        var buffer = Marshal.StringToHGlobalUni(new string('\0', 2048));
        try
        {
            SendMessageTimeout(hWnd, WM_GETTEXT, (IntPtr)2048, buffer, SMTO_ABORTIFHUNG, 400, out _);
            return (Marshal.PtrToStringUni(buffer) ?? string.Empty).TrimEnd('\0');
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>Writes a control's text over WM_SETTEXT -- marshalled by user32, same as the read.</summary>
    public static bool SetText(IntPtr hWnd, string value)
    {
        if (hWnd == IntPtr.Zero || string.IsNullOrEmpty(value)) return false;
        var buffer = Marshal.StringToHGlobalUni(value);
        try
        {
            SendMessageTimeout(hWnd, WM_SETTEXT, IntPtr.Zero, buffer, SMTO_ABORTIFHUNG, 400, out var result);
            return result != IntPtr.Zero;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public static List<IntPtr> Children(IntPtr parent)
    {
        var list = new List<IntPtr>();
        EnumChildWindows(parent, (h, _) => { list.Add(h); return true; }, IntPtr.Zero);
        return list;
    }

    public static IntPtr ParentOf(IntPtr hWnd) => hWnd == IntPtr.Zero ? IntPtr.Zero : GetParent(hWnd);

    public static bool IsVisible(IntPtr hWnd) => hWnd != IntPtr.Zero && IsWindowVisible(hWnd);

    /// <summary>The keyboard-focused control on the thread that owns <paramref name="hWnd"/>.</summary>
    public static IntPtr GetFocusedControl(IntPtr hWnd)
    {
        var threadId = GetWindowThreadProcessId(hWnd, out _);
        if (threadId == 0) return IntPtr.Zero;
        var gui = new GUITHREADINFO { cbSize = Marshal.SizeOf<GUITHREADINFO>() };
        return GetGUIThreadInfo(threadId, ref gui) ? gui.hwndFocus : IntPtr.Zero;
    }

    /// <summary>
    /// True when <paramref name="hWnd"/> sits inside a pane container, which is what separates xplorer2's
    /// folder panes from its mini-scrap list. Walks parent links only, so it never sends a message into
    /// xplorer2 and cannot block on a busy target.
    /// </summary>
    public static bool IsInsideMillerView(IntPtr hWnd)
    {
        var current = hWnd;
        for (var depth = 0; depth < 8 && current != IntPtr.Zero; depth++)
        {
            if (GetClassName(current).Equals(MillerClass, StringComparison.OrdinalIgnoreCase))
                return true;
            current = ParentOf(current);
        }
        return false;
    }

    /// <summary>
    /// The rectangle the inline card should dock to: xplorer2's content host, so the card hangs from its
    /// top-right corner, below the ribbon and the address bar and inside the window.
    /// </summary>
    /// <remarks>
    /// Not the focused pane. xplorer2 docks its side panels (tree, the 'How do I?...' hint column, preview)
    /// inside the same frame, so a pane's right edge moves by hundreds of pixels depending on which of them
    /// are currently shown -- and the app-side placer reads only dock.Right and dock.Top
    /// (InlineSearchWindowPositioner.CalculatePhysLeft / CalculatePhysTop), so an anchor that moves with the
    /// layout moves the card with it. Measured: the content host sits at 1510x797@278,334 inside a frame at
    /// 1526x1070@270,92, i.e. right edge 1788 and top 334, which is the corner the card belongs in.
    ///
    /// Falls back to the frame's extended bounds when no content host is found, and refuses a hidden
    /// window: while xplorer2 is parked its whole tree sits at -32000, which would otherwise place a card
    /// off screen.
    /// </remarks>
    public static bool TryGetDockBounds(IntPtr frameHwnd, out RECT rect)
    {
        rect = default;
        if (frameHwnd == IntPtr.Zero || IsIconic(frameHwnd)) return false;

        var bestArea = 0;
        foreach (var child in Children(frameHwnd))
        {
            if (!GetClassName(child).Equals(DockContentClass, StringComparison.OrdinalIgnoreCase)) continue;
            if (!IsVisible(child)) continue;
            if (!GetWindowRect(child, out var childRect)) continue;
            var area = (childRect.Right - childRect.Left) * (childRect.Bottom - childRect.Top);
            if (area <= bestArea) continue;
            bestArea = area;
            rect = childRect;
        }
        if (bestArea > 0) return true;

        if (DwmGetWindowAttribute(frameHwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out var frameRect, Marshal.SizeOf<RECT>()) == 0)
        {
            rect = frameRect;
            return true;
        }
        if (GetWindowRect(frameHwnd, out var windowRect))
        {
            rect = windowRect;
            return true;
        }
        return false;
    }
}
