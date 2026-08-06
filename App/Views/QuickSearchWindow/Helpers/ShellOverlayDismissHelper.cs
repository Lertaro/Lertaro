using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Lertaro.Core;

namespace Lertaro.App.Views.QuickSearchWindow.Helpers;

// The shell's light-dismiss overlays (Start Menu, Search, notification center, emoji panel,
// clipboard history, tray overflow, ...) don't hand over keyboard focus the normal way: once our
// window calls Show()/SetForegroundWindow, GetForegroundWindow() reports US as foreground while the
// overlay silently keeps receiving every keystroke. No API reveals that mismatch after the fact, so
// the only reliable move is to detect the overlay while it's still truthfully foreground -- before
// we show anything -- and dismiss it with ESC, which is exactly what light-dismiss surfaces are
// built to close on (and costs the user no state, unlike ESC into an arbitrary app's dialog, which
// is why this deliberately targets shell overlays only and must not be broadened to any window).
internal static class ShellOverlayDismissHelper
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    [DllImport("user32.dll")] private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, IntPtr dwExtraInfo);
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)] private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);

    private const byte VK_ESCAPE = 0x1B;
    private const byte VK_LCONTROL = 0xA2;
    private const byte VK_RCONTROL = 0xA3;
    private const byte VK_LMENU = 0xA4;
    private const byte VK_RMENU = 0xA5;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;

    // Processes that exist solely to host shell overlay experiences. SearchHost/StartMenuExperienceHost
    // are the original Start Menu case; ShellExperienceHost (notification/action center) and ShellHost
    // (Win11 quick settings) host the same kind of focus-holding light-dismiss surfaces.
    private static readonly string[] ShellOverlayProcessNames = { "SearchHost", "StartMenuExperienceHost", "ShellExperienceHost", "ShellHost" };

    // Explorer's own XAML-island overlays: the taskbar tray-overflow flyout and the task-view/snap
    // island. Explorer can't be matched by process name (it owns the taskbar and desktop too), so
    // these are matched by their dedicated top-level window classes instead.
    private static readonly string[] ExplorerOverlayClassNames = { "TopLevelWindowForOverflowXamlIsland", "XamlExplorerHostIslandWindow" };

    public static string TryGetProcessName(uint pid)
    {
        try { return Process.GetProcessById((int)pid).ProcessName; }
        catch { return "?"; }
    }

    private static bool IsShellOverlayFocused()
    {
        var fgHwnd = GetForegroundWindow();
        if (fgHwnd == IntPtr.Zero) return false;

        var sbClass = new StringBuilder(256);
        GetClassName(fgHwnd, sbClass, sbClass.Capacity);
        var fgClassName = sbClass.ToString();

        // A bare CoreWindow as THE foreground window only ever belongs to a shell light-dismiss
        // overlay (Start, Search, notification center, emoji panel / clipboard history via
        // TextInputHost, ...). Real UWP apps put an ApplicationFrameWindow host in the foreground,
        // never their inner CoreWindow, so this can't misfire on ordinary applications.
        if (fgClassName == "Windows.UI.Core.CoreWindow") return true;

        if (ExplorerOverlayClassNames.Any(c => string.Equals(c, fgClassName, StringComparison.OrdinalIgnoreCase)))
            return true;

        GetWindowThreadProcessId(fgHwnd, out var fgPid);
        var fgProcessName = TryGetProcessName(fgPid);
        return ShellOverlayProcessNames.Any(p => string.Equals(p, fgProcessName, StringComparison.OrdinalIgnoreCase));
    }

    private static void ReleaseIfHeld(byte vk, bool extended)
    {
        if ((GetAsyncKeyState(vk) & 0x8000) != 0)
            keybd_event(vk, 0, KEYEVENTF_KEYUP | (extended ? KEYEVENTF_EXTENDEDKEY : 0), IntPtr.Zero);
    }

    public static void DismissOverlayIfForeground()
    {
        for (var i = 0; i < 3; i++)
        {
            if (!IsShellOverlayFocused())
                return;

            Logger.Log($"[ShellOverlayDismissHelper] Shell overlay holds foreground -- dismissing (attempt {i + 1})", LogLevel.Debug);

            // The summon hotkey's modifiers are often still physically held when this runs (e.g. the
            // second tap of a double-Ctrl hotkey), and the injected ESC merges with them: Ctrl+ESC is
            // the system shortcut that OPENS the Start Menu, Alt+ESC cycles window z-order. Release
            // any held Ctrl/Alt first so the overlay receives a plain ESC. Win is deliberately left
            // held: injecting a Win key-up could itself pop the Start Menu (a lone Win tap opens it),
            // while Win+ESC is inert -- and the ESC press marks the Win tap as "used", which prevents
            // the Start Menu from opening on the user's real Win release too.
            ReleaseIfHeld(VK_LCONTROL, extended: false);
            ReleaseIfHeld(VK_RCONTROL, extended: true);
            ReleaseIfHeld(VK_LMENU, extended: false);
            ReleaseIfHeld(VK_RMENU, extended: true);

            keybd_event(VK_ESCAPE, 0, 0, IntPtr.Zero);
            keybd_event(VK_ESCAPE, 0, KEYEVENTF_KEYUP, IntPtr.Zero);

            // Wait up to 100ms (in 20ms steps) for the focus to transition away from the overlay
            for (var j = 0; j < 5; j++)
            {
                Thread.Sleep(20);
                if (!IsShellOverlayFocused())
                    break;
            }
        }
    }
}
