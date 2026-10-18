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

    // Fires as soon as the Win32 HWND exists, before WPF's first layout/render pass -- which is the only point
    // RenderMode can still be set usefully: by HandleLoaded, WPF may already have created the hardware D3D
    // device for this window's HwndTarget, and RenderMode governs future frames, not one that is already
    // backing the window.
    public void HandleSourceInitialized()
    {
        // The window is the card itself, so its rounded corners are the window's real shape rather than a
        // painting inside it -- the same call the notification windows make through WindowSurface. On
        // Windows 10 the attribute does not exist, the call fails, and the corners stay square, which is
        // this project's accepted fallback.
        DwmWindowCorners.ApplyRound(_window);

        // Composed on the CPU. This window is created once at startup and only ever Hidden, never Closed, so
        // its DirectX composition surface stays alive for the whole process lifetime, which NVIDIA Advanced
        // Optimus treats as "GPU in use" and refuses to hot-switch around (GitHub #82). The other half is the
        // summon: a hardware target has to create and resize a surface, and a card that is being shown across a
        // DPI boundary does that while it is already on screen. Software composition was measured never to be
        // slower for a card (scratch/blackprobe) and it keeps ClearType, which is more than the layered-window
        // route this app gave up can say.
        //
        // It is deliberately not gated on EnableHardwareAcceleration any more: that setting is an opt-out for
        // the windows the user sits inside (Settings, the full search window, QuickLook), and it cannot be the
        // reason a 200ms unpainted gap opens on the ones they only ever summon. It still governs those.
        CardRenderMode.Apply(_window);
    }

    public void HandleLoaded()
    {
        Logger.Log("[QuickSearchWindow] Window loaded. Registering hotkey and triggering index build.", LogLevel.Debug);

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

        // Position the window

        // A no-op here: the warm-up window is guarded against being positioned at all until a summon takes
        // it over (see QuickSearchWindow._suppressPositioning, which records what went wrong when it was
        // not). The summon positions the window twice around its own Show.
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
