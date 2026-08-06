using System.Runtime.InteropServices;
using System.Windows;

namespace Lertaro.App.Services.QuickPanel;

// Where the panel puts itself, and the P/Invokes that answer that. Split out of QuickPanelManager.cs
// purely to keep that file under the repo's per-file line limit; this is the one part of the manager
// that is about geometry rather than lifetime.
public sealed partial class QuickPanelManager
{
    // Floors for the quarter-of-the-host sizing: a panel docked to a small window still needs enough
    // room for a row to read as a row.
    // A quarter of the host's AREA, which is half of each side rather than a quarter: a quarter per
    // side would come to a sixteenth of the window, which is what it looked like.
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
    /// <remarks>
    /// GetWindowRect is in physical pixels while WPF's Left/Top are in device-independent units, so the
    /// rect is scaled by the host's own DPI rather than this window's: on a mixed-DPI setup the two can
    /// differ, and it is the host's corner being aimed at. Falls back to the working area's corner when
    /// there is no usable foreground window, which is what happens if the panel is triggered from the
    /// desktop itself.
    /// </remarks>
    private void PositionAgainst(IntPtr host)
    {
        if (_window == null) return;

        var margin = 12.0;

        if (host != IntPtr.Zero && GetWindowRect(host, out var rect))
        {
            var dpi = GetDpiForWindow(host);
            var scale = dpi > 0 ? 96.0 / dpi : 1.0;

            var right = rect.Right * scale;
            var bottom = rect.Bottom * scale;
            var hostWidth = (rect.Right - rect.Left) * scale;
            var hostHeight = (rect.Bottom - rect.Top) * scale;

            // A quarter of the window it docks to, floored so it stays usable against a small host.
            // Both axes, as asked: the panel is a quarter of the window it docks to. Fewer rows than fit
            // simply leave the rest of the panel empty, which is what a fixed proportion means.
            _window.Width = Math.Max(MinPanelWidth, hostWidth * PanelSideFactor);
            _window.Height = Math.Max(MinPanelHeight, hostHeight * PanelSideFactor);

            _window.Left = right - _window.Width - margin;
            _window.Top = bottom - _window.Height - margin;
            return;
        }

        var work = SystemParameters.WorkArea;
        _window.Left = work.Right - _window.Width - margin;
        _window.Top = work.Bottom - _window.Height - margin;
    }
}
