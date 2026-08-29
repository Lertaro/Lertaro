using System.Windows;
using System.Windows.Interop;

namespace Lertaro.PluginSdk.Windows;

// Kept in the SDK so every plugin window gets the same custom-chrome behavior without referencing
// host-only App helpers or making each plugin duplicate Win32 interop.
internal static class PluginWindowNativeBehavior
{
    private const int WM_SYSCOMMAND = 0x0112;
    private const int SC_KEYMENU = 0xF100;
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    public static void Configure(PluginWindow window, PluginWindowMode mode)
    {
        AttachSystemMenuBlocker(window);
        if (mode == PluginWindowMode.Dialog)
        {
            window.Topmost = true;
            window.ShowInTaskbar = false;
            AttachAltTabExcluder(window);
        }
        else
        {
            window.Topmost = false;
            window.ShowInTaskbar = true;
        }
    }

    private static void AttachSystemMenuBlocker(Window window)
    {
        void Hook(HwndSource source) => source.AddHook((IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled) =>
        {
            if (message == WM_SYSCOMMAND && ((int)wParam & 0xFFF0) == SC_KEYMENU)
                handled = true;
            return IntPtr.Zero;
        });

        if (PresentationSource.FromVisual(window) is HwndSource source)
            Hook(source);
        else
            window.SourceInitialized += (_, _) =>
            {
                if (PresentationSource.FromVisual(window) is HwndSource initializedSource)
                    Hook(initializedSource);
            };
    }

    private static void AttachAltTabExcluder(Window window)
    {
        void Apply()
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;

            var exStyle = GetWindowLongPtr(hwnd, GWL_EXSTYLE);
            SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(exStyle.ToInt64() | WS_EX_TOOLWINDOW));
        }

        if (PresentationSource.FromVisual(window) != null) Apply();
        else window.SourceInitialized += (_, _) => Apply();
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
}
