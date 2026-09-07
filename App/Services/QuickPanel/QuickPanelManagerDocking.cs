using System.Runtime.InteropServices;
using System.Windows.Media;

namespace Lertaro.App.Services.QuickPanel;

// Where the panel puts itself, and the P/Invokes that answer that. Split out of QuickPanelManager.cs
// purely to keep that file under the repo's per-file line limit; this is the one part of the manager
// that is about geometry rather than lifetime.
public sealed partial class QuickPanelManager
{
    private const double PanelSideFactor = 0.5;

    private const double MinPanelWidth = 280;
    private const double MinPanelHeight = 200;

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    /// <summary>Docks the panel inside the host window's bottom-right corner.</summary>
    private void PositionAgainst(IntPtr host)
    {
        if (_window == null) return;

        var currentDpi = VisualTreeHelper.GetDpi(_window);
        var currentScaleX = currentDpi.DpiScaleX > 0 ? currentDpi.DpiScaleX : 1.0;
        var currentScaleY = currentDpi.DpiScaleY > 0 ? currentDpi.DpiScaleY : 1.0;

        if (host != IntPtr.Zero && GetWindowRect(host, out var rect))
        {
            var dpi = GetDpiForWindow(host);
            var screen = Screen.FromHandle(host);
            var wa = screen.WorkingArea;

            var (width, height, left, top) = CalculateDockPosition(
                rect.Left, rect.Top, rect.Right, rect.Bottom,
                dpi,
                wa.Left, wa.Top, wa.Width, wa.Height,
                currentScaleX, currentScaleY);

            _window.Width = width;
            _window.Height = height;
            _window.Left = left;
            _window.Top = top;
            return;
        }

        var mouseScreen = Screen.FromPoint(Control.MousePosition);
        var mouseWa = mouseScreen.WorkingArea;
        var fallbackDpi = (uint)currentDpi.PixelsPerInchX;
        var (fbWidth, fbHeight, fbLeft, fbTop) = CalculateDockPosition(
            mouseWa.Left, mouseWa.Top, mouseWa.Right, mouseWa.Bottom,
            fallbackDpi,
            mouseWa.Left, mouseWa.Top, mouseWa.Width, mouseWa.Height,
            currentScaleX, currentScaleY);

        _window.Width = fbWidth;
        _window.Height = fbHeight;
        _window.Left = fbLeft;
        _window.Top = fbTop;
    }

    internal static (double Width, double Height, double Left, double Top) CalculateDockPosition(
        int hostLeft, int hostTop, int hostRight, int hostBottom,
        uint hostDpi,
        int waLeft, int waTop, int waWidth, int waHeight,
        double currentDpiScaleX, double currentDpiScaleY)
    {
        const double margin = 12.0;
        var hostScale = hostDpi > 0 ? hostDpi / 96.0 : 1.0;

        var hostWidthDip = (hostRight - hostLeft) / hostScale;
        var hostHeightDip = (hostBottom - hostTop) / hostScale;

        var panelWidthDip = Math.Max(MinPanelWidth, hostWidthDip * PanelSideFactor);
        var panelHeightDip = Math.Max(MinPanelHeight, hostHeightDip * PanelSideFactor);

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

        var scaleX = currentDpiScaleX > 0 ? currentDpiScaleX : 1.0;
        var scaleY = currentDpiScaleY > 0 ? currentDpiScaleY : 1.0;

        return (panelWidthDip, panelHeightDip, targetPhysLeft / scaleX, targetPhysTop / scaleY);
    }
}
