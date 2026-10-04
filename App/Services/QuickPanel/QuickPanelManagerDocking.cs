using System.Runtime.InteropServices;

namespace Lertaro.App.Services.QuickPanel;

// Where the panel puts itself, and the P/Invokes that answer that. Split out of QuickPanelManager.cs
// purely to keep that file under the repo's per-file line limit; this is the one part of the manager
// that is about geometry rather than lifetime.
public sealed partial class QuickPanelManager
{
    private const double PanelSideFactor = 0.5;

    private const double MinPanelWidth = 280;
    private const double MinPanelHeight = 200;

    /// <summary>
    /// The largest the panel makes itself when it sizes to the window it docks to.
    /// </summary>
    /// <remarks>
    /// A cap on the AUTOMATIC size only, and deliberately not a MaxWidth/MaxHeight on the window: half of
    /// a maximized 4K window is a panel with more room than a screenful of files can fill, and the point
    /// of sizing to the host was that the panel suits the app it is over -- not that it grows without
    /// limit along with it. Keeping it on the automatic path is also what keeps it out of the user's way:
    /// a manual drag of the resize grip goes wherever it is taken, and this is not consulted again until
    /// the next summon sizes the panel to a host.
    /// </remarks>
    internal const double MaxAutoWidth = 650;
    internal const double MaxAutoHeight = 500;

    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);
    [DllImport("user32.dll")] private static extern IntPtr GetDesktopWindow();
    [DllImport("user32.dll")] private static extern IntPtr GetShellWindow();
    [DllImport("Shcore.dll")] private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    private const int MDT_EFFECTIVE_DPI = 0;
    private const uint MONITOR_DEFAULTTONEAREST = 2;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;

    private IntPtr _lastHost;
    private bool _dpiSubscribed;

    /// <summary>True while the automatic placement is writing the window's size, so it is not mistaken for the user's.</summary>
    /// <remarks>
    /// The other half of the guard in QuickPanelWindow.Window_SizeChanged. That one tells a content
    /// re-layout from a real resize; this one tells the panel's own initial placement from either. Both are
    /// needed because both raise SizeChanged with a genuinely different size, and recording either would
    /// pin a size nobody chose.
    /// </remarks>
    private bool _positioning;

    /// <summary>True once the user has moved or resized this summon by hand.</summary>
    /// <remarks>
    /// Per summon, like everything else about this window -- it is built fresh each time the panel opens
    /// (see QuickPanelManager), so the flag cannot leak into the next one. What DOES carry across is the
    /// size, which <see cref="MarkUserSized"/> writes down.
    /// </remarks>
    private bool _userSized;

    /// <summary>
    /// Records that the user has taken over the panel's size, and remembers it for the next summon.
    /// </summary>
    /// <remarks>
    /// Called from the window's own drag/resize handling. Saving here rather than on close, because the
    /// panel closes by losing the foreground as often as by Escape, and a size that came back wrong on the
    /// next summon would read as broken.
    ///
    /// Only the SIZE is stored, not the position: the panel exists to dock against whatever window is in
    /// front, so re-opening it wherever it happened to be left would defeat the point. The size is a
    /// property of the panel; the position is a property of the host.
    /// </remarks>
    internal void MarkUserSized()
    {
        if (_window == null) return;

        // The placement itself writes Width/Height, and that write reaches here through the window's own
        // SizeChanged. Recording it would store the automatic size as though the user had chosen it, which
        // is the same defect from the other side -- see _positioning.
        if (_positioning) return;

        _userSized = true;

        var settings = Core.UserSettings.Load();
        settings.QuickPanel.UserWidth = Math.Max(MinPanelWidth, _window.Width);
        settings.QuickPanel.UserHeight = Math.Max(MinPanelHeight, _window.Height);
        settings.Save();
    }

    internal static bool IsDesktopOrShellWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return true;
        if (hwnd == GetDesktopWindow() || hwnd == GetShellWindow()) return true;

        if (InlineSearchManager.Instance?.ExplorerTracker?.IsDesktop == true &&
            InlineSearchManager.Instance.ExplorerTracker.ActiveHwnd == hwnd)
        {
            return true;
        }

        if (Core.Hook.ExplorerNativeHooks.IsDesktopWindow(hwnd, out var className))
            return true;

        return className.Equals("Shell_TrayWnd", StringComparison.OrdinalIgnoreCase) ||
               className.Equals("Shell_SecondaryTrayWnd", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Docks the panel inside the host window's bottom-right corner, or the active monitor on desktop.</summary>
    /// <remarks>
    /// Does nothing once the user has sized or moved the panel by hand for this summon. Everything it
    /// computes is the AUTOMATIC placement, and the one thing a user's own drag is not is automatic: a
    /// re-run (a DPI change, a re-show) would otherwise snap the panel back to half the host window at the
    /// docked corner, discarding a size they chose. See <see cref="MarkUserSized"/>.
    /// </remarks>
    private void PositionAgainst(IntPtr host)
    {
        if (_window == null) return;

        _lastHost = host;
        if (!_dpiSubscribed)
        {
            _dpiSubscribed = true;

            // DpiChanged, guarded twice, and both guards are load-bearing.
            //
            // It is a ROUTED event, so it bubbles: a DPI change anywhere inside the panel reaches this
            // handler with the window as the sender. That is what made the panel snap back to its
            // automatic size for no visible reason -- the stack recorded it happening from
            // Image.MeasureOverride, i.e. a thumbnail being re-measured as the pointer passed over it, and
            // every one of those re-ran the whole placement below.
            //
            // So the check is on OriginalSource: only the window's own DPI change is this handler's
            // business. And the size is not rewritten unless the panel is up, since placing a hidden
            // window is what re-docking on a re-show is for.
            _window.DpiChanged += (_, e) =>
            {
                if (_window == null || !_window.IsVisible) return;
                if (!ReferenceEquals(e.OriginalSource, _window)) return;
                PositionAgainst(_lastHost);
            };

            _window.Closed += (_, _) => _dpiSubscribed = false;
        }

        // The user's own placement wins, and is re-applied rather than skipped: a DPI change moves the
        // window in physical pixels, so the DIP values have to be written again to keep it where it looks
        // like it is. Nothing here recomputes anything -- it is the same DIPs the user dragged to.
        if (_userSized)
        {
            _positioning = true;
            try
            {
                _window.Width = Math.Max(MinPanelWidth, _window.Width);
                _window.Height = Math.Max(MinPanelHeight, _window.Height);
            }
            finally
            {
                _positioning = false;
            }
            return;
        }

        double width;
        double height;
        double targetPhysLeft;
        double targetPhysTop;
        double targetDpiScale;

        // A size the user chose survives the summon: it is theirs, and the automatic figure is only ever a
        // stand-in for someone who has not chosen one. The POSITION is still docked either way, which is
        // the whole point of the panel -- see MarkUserSized for why only the size is remembered.
        var stored = Core.UserSettings.Load().QuickPanel;
        var (userWidth, userHeight) = stored.HasUserSize
            ? (Math.Max(MinPanelWidth, stored.UserWidth), Math.Max(MinPanelHeight, stored.UserHeight))
            : (0.0, 0.0);

        var isDesktop = IsDesktopOrShellWindow(host);

        if (!isDesktop && host != IntPtr.Zero && GetWindowRect(host, out var rect) && (rect.Right - rect.Left > 100 && rect.Bottom - rect.Top > 100))
        {
            var dpi = GetDpiForWindow(host);
            var screen = Screen.FromHandle(host);
            var wa = screen.WorkingArea;
            targetDpiScale = dpi > 0 ? dpi / 96.0 : 1.0;

            (width, height, targetPhysLeft, targetPhysTop) = CalculatePhysicalDockPosition(
                rect.Left, rect.Top, rect.Right, rect.Bottom,
                dpi,
                wa.Left, wa.Top, wa.Width, wa.Height,
                userWidth, userHeight);
        }
        else
        {
            Screen mouseScreen;
            IntPtr targetMonitor;
            var curHwnd = new System.Windows.Interop.WindowInteropHelper(_window).Handle;
            if (_window.IsVisible && curHwnd != IntPtr.Zero)
            {
                mouseScreen = Screen.FromHandle(curHwnd);
                targetMonitor = MonitorFromWindow(curHwnd, MONITOR_DEFAULTTONEAREST);
            }
            else
            {
                var mousePos = Control.MousePosition;
                mouseScreen = Screen.FromPoint(mousePos);
                targetMonitor = MonitorFromPoint(new POINT { X = mousePos.X, Y = mousePos.Y }, MONITOR_DEFAULTTONEAREST);
            }

            var mouseWa = mouseScreen.WorkingArea;
            var dpi = GetMonitorDpi(targetMonitor);
            targetDpiScale = dpi > 0 ? dpi / 96.0 : 1.0;

            (width, height, targetPhysLeft, targetPhysTop) = CalculatePhysicalDockPosition(
                mouseWa.Left, mouseWa.Top, mouseWa.Right, mouseWa.Bottom,
                dpi,
                mouseWa.Left, mouseWa.Top, mouseWa.Width, mouseWa.Height,
                userWidth, userHeight);
        }

        // These two writes are this class's own placement, not the user's. The flag spans them so the
        // window's SizeChanged handler does not record them as a choice -- see MarkUserSized.
        _positioning = true;
        try
        {
            _window.Width = width;
            _window.Height = height;
            _window.Left = targetPhysLeft / targetDpiScale;
            _window.Top = targetPhysTop / targetDpiScale;
        }
        finally
        {
            _positioning = false;
        }

        var hwnd = new System.Windows.Interop.WindowInteropHelper(_window).EnsureHandle();
        if (hwnd != IntPtr.Zero)
        {
            SetWindowPos(hwnd, IntPtr.Zero,
                (int)Math.Round(targetPhysLeft), (int)Math.Round(targetPhysTop), 0, 0,
                SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
        }
    }

    /// <summary>Where the panel goes, and how big, for a host occupying the given rectangle.</summary>
    /// <param name="userWidthDip">
    /// A size the user dragged the panel to, or zero for "they never have". Non-zero replaces the
    /// automatic half-of-the-host figure -- and is deliberately NOT clamped to
    /// <see cref="MaxAutoWidth"/>/<see cref="MaxAutoHeight"/>, which cap the automatic size only.
    /// </param>
    internal static (double Width, double Height, double PhysLeft, double PhysTop) CalculatePhysicalDockPosition(
        int hostLeft, int hostTop, int hostRight, int hostBottom,
        uint hostDpi,
        int waLeft, int waTop, int waWidth, int waHeight,
        double userWidthDip = 0, double userHeightDip = 0)
    {
        const double margin = 12.0;
        var hostScale = hostDpi > 0 ? hostDpi / 96.0 : 1.0;

        var hostWidthDip = (hostRight - hostLeft) / hostScale;
        var hostHeightDip = (hostBottom - hostTop) / hostScale;

        var panelWidthDip = userWidthDip > 0
            ? Math.Max(MinPanelWidth, userWidthDip)
            : Math.Clamp(hostWidthDip * PanelSideFactor, MinPanelWidth, MaxAutoWidth);
        var panelHeightDip = userHeightDip > 0
            ? Math.Max(MinPanelHeight, userHeightDip)
            : Math.Clamp(hostHeightDip * PanelSideFactor, MinPanelHeight, MaxAutoHeight);

        var physPanelWidth = panelWidthDip * hostScale;
        var physPanelHeight = panelHeightDip * hostScale;
        var physMargin = margin * hostScale;

        var targetPhysLeft = hostRight - physPanelWidth - physMargin;
        var targetPhysTop = hostBottom - physPanelHeight - physMargin;

        if (waWidth > 0 && waHeight > 0)
        {
            targetPhysLeft = Math.Clamp(targetPhysLeft, waLeft, waLeft + waWidth - physPanelWidth);
            targetPhysTop = Math.Clamp(targetPhysTop, waTop, waTop + waHeight - physPanelHeight);
        }

        return (panelWidthDip, panelHeightDip, targetPhysLeft, targetPhysTop);
    }

    internal static (double Width, double Height, double Left, double Top) CalculateDockPosition(
        int hostLeft, int hostTop, int hostRight, int hostBottom,
        uint hostDpi,
        int waLeft, int waTop, int waWidth, int waHeight,
        double currentDpiScaleX, double currentDpiScaleY)
    {
        var (width, height, physLeft, physTop) = CalculatePhysicalDockPosition(
            hostLeft, hostTop, hostRight, hostBottom,
            hostDpi,
            waLeft, waTop, waWidth, waHeight);

        var scaleX = currentDpiScaleX > 0 ? currentDpiScaleX : 1.0;
        var scaleY = currentDpiScaleY > 0 ? currentDpiScaleY : 1.0;

        return (width, height, physLeft / scaleX, physTop / scaleY);
    }

    private static uint GetMonitorDpi(IntPtr hMonitor)
    {
        if (hMonitor != IntPtr.Zero && GetDpiForMonitor(hMonitor, MDT_EFFECTIVE_DPI, out var dpiX, out var dpiY) == 0 && dpiX > 0)
            return dpiX;
        return 96;
    }
}
