using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Point = System.Windows.Point;

namespace Lertaro.App.Views.QuickLook;

// Airspace workaround: the QuickLook window is layered (AllowsTransparency=true), so a native preview
// HwndHost can't render inside it. Host it in a separate non-layered window laid exactly over the content
// area and kept in sync with the owner window's moves/resizes.
internal sealed class PreviewOverlay
{
    private readonly Window _owner;
    private readonly FrameworkElement _anchor;
    private Window? _overlay;
    private HwndHost? _host;

    public PreviewOverlay(Window owner, FrameworkElement anchor)
    {
        _owner = owner;
        _anchor = anchor;
    }

    public void Show(HwndHost host)
    {
        Clear();
        _host = host;
        _overlay = new Window
        {
            Owner = _owner,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = false,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            ShowActivated = false,
            Content = host,
        };
        _overlay.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "CardBackground");

        _owner.LocationChanged += OnChanged;
        _owner.SizeChanged += OnSizeChanged;
        _anchor.SizeChanged += OnSizeChanged;

        _overlay.Show();
        Reposition();
        // The content area may still be laying out; reposition once more after this dispatch settles.
        _overlay.Dispatcher.BeginInvoke(new Action(Reposition), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    public void Clear()
    {
        _owner.LocationChanged -= OnChanged;
        _owner.SizeChanged -= OnSizeChanged;
        _anchor.SizeChanged -= OnSizeChanged;

        if (_overlay != null)
        {
            _overlay.Content = null;
            _overlay.Close();
            _overlay = null;
        }
        _host?.Dispose();
        _host = null;
    }

    private void OnChanged(object? sender, EventArgs e) => Reposition();
    private void OnSizeChanged(object? sender, SizeChangedEventArgs e) => Reposition();

    private void Reposition()
    {
        if (_overlay == null || !_owner.IsVisible || !_anchor.IsVisible || _anchor.ActualWidth <= 0)
            return;
        try
        {
            // Device-pixel screen rect of the content area; SetWindowPos also takes device pixels, so no
            // DPI conversion is needed and it stays correct across per-monitor DPI.
            var topLeft = _anchor.PointToScreen(new Point(0, 0));
            var bottomRight = _anchor.PointToScreen(new Point(_anchor.ActualWidth, _anchor.ActualHeight));
            var hwnd = new WindowInteropHelper(_overlay).Handle;
            if (hwnd == IntPtr.Zero) return;
            SetWindowPos(hwnd, IntPtr.Zero,
                (int)topLeft.X, (int)topLeft.Y,
                (int)(bottomRight.X - topLeft.X), (int)(bottomRight.Y - topLeft.Y),
                SWP_NOZORDER | SWP_NOACTIVATE);
        }
        catch
        {
            // Anchor not connected to a source yet / transient — a later move/size event repositions.
        }
    }

    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);
}
