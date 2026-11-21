using System.Windows;
using System.Windows.Interop;

namespace Lertaro.App.Helpers.Visuals;

/// <summary>
/// Blocks the native Alt+Space system menu on custom-chrome windows and optionally blocks Alt+F4. Settings
/// and Full Search allow close through blockClose=false.
///
/// Quick Search is a persistent MainWindow that must hide rather than close. OnExplicitShutdown would
/// otherwise leave the process running without its launcher window. Inline keys directed to the host are
/// intercepted separately by the Hook.
/// </summary>
public static class SystemMenuBlocker
{
    private const int WM_SYSCOMMAND = 0x0112;
    private const int SC_KEYMENU = 0xF100;
    private const int SC_CLOSE = 0xF060;

    /// <param name="blockClose">
    /// Whether Alt+F4 is blocked along with Alt+Space. The two are separate subcommands, and a window
    /// that only wants the system menu gone has no reason to give up its close key with it: pass false
    /// and Alt+F4 keeps working normally. True for the windows whose lifecycle genuinely cannot survive
    /// an OS-driven close.
    /// </param>
    public static void Attach(Window window, bool blockClose = true)
    {
        if (PresentationSource.FromVisual(window) is HwndSource hwndSource)
        {
            Hook(hwndSource, blockClose);
        }
        else
        {
            window.SourceInitialized += (s, e) =>
            {
                if (PresentationSource.FromVisual(window) is HwndSource src)
                    Hook(src, blockClose);
            };
        }
    }

    private static void Hook(HwndSource hwndSource, bool blockClose) => hwndSource.AddHook((IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) =>
    {
        if (msg == WM_SYSCOMMAND)
        {
            var command = (int)wParam & 0xFFF0;
            if (command == SC_KEYMENU || (blockClose && command == SC_CLOSE))
                handled = true;
        }
        return IntPtr.Zero;
    });
}
