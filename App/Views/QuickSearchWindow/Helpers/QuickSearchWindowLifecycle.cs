using System.Windows;
using System.Windows.Interop;
using Lertaro.App.Helpers.Visuals;
using Lertaro.App.Services.Theme;
using Lertaro.App.Views.InlineSearchWindow.Helpers;
using Lertaro.Core;

namespace Lertaro.App.Views.QuickSearchWindow.Helpers;

// Handles QuickSearchWindow's SourceInitialized/Loaded startup sequence -- split out of the window
// itself to keep that file under the line-count limit.
public class QuickSearchWindowLifecycle
{
    private readonly Lertaro.App.QuickSearchWindow _window;
    private readonly Action _handleTaskbarCreated;
    private bool _isFirstLoad = true;

    private static readonly int WM_TASKBARCREATED = InlineSearchWindowNativeMethods.RegisterWindowMessage("TaskbarCreated");

    public QuickSearchWindowLifecycle(Lertaro.App.QuickSearchWindow window, Action handleTaskbarCreated)
    {
        _window = window;
        _handleTaskbarCreated = handleTaskbarCreated;
    }

    // Fires as soon as the Win32 HWND exists, before WPF's first layout/render pass -- the earliest
    // point CompositionTarget.RenderMode can be set. This window is created once at startup and only
    // ever Hidden, never Closed -- its DirectX composition surface stays alive for the whole process
    // lifetime, which NVIDIA Advanced Optimus treats as "GPU in use" and refuses to hot-switch around
    // (GitHub #82). Forcing software rendering on just this one window's HwndTarget avoids that,
    // without touching SettingsWindow/SearchWindow/etc. (which are properly closed and don't trigger
    // this). Setting it here rather than in HandleLoaded (which runs after the window has already
    // been shown once) matters: by Loaded, WPF may already have created the hardware D3D device for
    // this HwndTarget, and RenderMode only governs FUTURE frames, not retroactively releasing one
    // that's already backing this window. Opt-out setting since it costs this window its hardware
    // acceleration.
    public void HandleSourceInitialized()
    {
        if (!UserSettings.Load().EnableHardwareAcceleration
            && PresentationSource.FromVisual(_window) is HwndSource hwndSource
            && hwndSource.CompositionTarget is HwndTarget hwndTarget)
        {
            hwndTarget.RenderMode = RenderMode.SoftwareOnly;
        }
    }

    public void HandleLoaded()
    {
        Logger.Log("[QuickSearchWindow] Window loaded. Applying Alt+Tab hiding, shell hooks and theme effects.", LogLevel.Debug);

        // Hide from Alt+Tab by setting WS_EX_TOOLWINDOW
        var hwnd = new WindowInteropHelper(_window).Handle;
        if (hwnd != IntPtr.Zero)
        {
            var exStyle = InlineSearchWindowNativeMethods.GetWindowLongPtr(hwnd, InlineSearchWindowNativeMethods.GWL_EXSTYLE);
            var newExStyle = new IntPtr(exStyle.ToInt64() | InlineSearchWindowNativeMethods.WS_EX_TOOLWINDOW);
            InlineSearchWindowNativeMethods.SetWindowLongPtr(hwnd, InlineSearchWindowNativeMethods.GWL_EXSTYLE, newExStyle);
        }

        // Re-add the tray icon if explorer.exe restarts mid-session (crash, shell update) -- Windows
        // silently drops every previously-registered tray icon and broadcasts this message so still-
        // running apps know to re-add theirs.
        if (PresentationSource.FromVisual(_window) is HwndSource hwndSource)
        {
            hwndSource.AddHook((IntPtr wnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) =>
            {
                if (msg == WM_TASKBARCREATED)
                    _handleTaskbarCreated();
                return IntPtr.Zero;
            });
        }

        if (ThemeManager.Instance.ActiveTheme != null)
        {
            WindowEffectHelper.ApplyThemeEffects(_window, ThemeManager.Instance.ActiveTheme);
        }

        // Position the window

        _window.PositionWindow();

        // Focus search box or hide on first launch

        if (_isFirstLoad)
        {
            _isFirstLoad = false;
            _window.Hide();
        }

        else
        {
            _window.TxtSearch.Focus();
        }
    }
}
