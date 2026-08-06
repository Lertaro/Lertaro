using System.Windows;
using System.Windows.Interop;

namespace Lertaro.App.Helpers.Visuals;

/// <summary>
/// Blocks two OS-level WM_SYSCOMMAND triggers: Alt+Space (SC_KEYMENU), which pops up the OS-drawn
/// system menu (Restore/Move/Size/Minimize/Maximize/Close) as a jarring blank box clipped by the
/// window's own borderless/rounded corners instead of a real title bar; and Alt+F4 (SC_CLOSE), which
/// would let the OS close the window out from under the app's own show/hide lifecycle. Every other
/// WM_SYSCOMMAND subcommand is left untouched.
/// </summary>
/// <remarks>
/// Every custom-chrome window uses this, since none of them has a real title bar for the system menu
/// to belong to. They differ only in whether Alt+F4 goes with it: the settings and full search windows
/// pass blockClose: false, because they are ordinary windows the user opens and is done with and
/// closing them is a perfectly good thing to want.
///
/// The quick window is the one that genuinely cannot do without this, being the application's
/// MainWindow with no ShutdownMode set, so letting Alt+F4 through would take the whole tray app down
/// with it. Attaching it to the inline window has never reliably covered that one: unresolved, and
/// left as it is rather than pursued here.
/// </remarks>
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
