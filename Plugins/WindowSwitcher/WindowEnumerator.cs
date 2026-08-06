using System.Runtime.InteropServices;

namespace Lertaro.Plugins.WindowSwitcher;

// Enumerates the same set of top-level windows the OS's own Alt+Tab switcher shows. The actual
// EnumWindows/DWM calls can't be unit tested without a live desktop, so the eligibility decision
// itself is pulled out into the pure IsAltTabEligible(...) method below -- that's what the tests
// exercise; this class is just the P/Invoke plumbing that gathers its inputs per window.
public static class WindowEnumerator
{
    public sealed record SwitchableWindow(IntPtr Handle, string Title, int ProcessId);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    // GetWindowTextLength/GetWindowText are documented as avoiding cross-process message-passing when
    // the target has a normal cached caption -- but that's not universally true in practice (this is
    // long-established lore among anyone who's written a window enumerator/task switcher), and this
    // whole method runs synchronously on the UI thread: IInstantResultProvider.GetInstantResults is
    // documented as "cheap and synchronous" throughout this codebase (see SearchExecutionEngine's own
    // comment), because the host never offloads it to a background thread. One truly hung window
    // anywhere on the user's desktop -- not even one of Lertaro's own -- must not be able to freeze
    // the whole app while GetSwitchableWindows enumerates past it. SendMessageTimeout with
    // SMTO_ABORTIFHUNG is the well-established defensive alternative; this codebase already relies on
    // the exact same technique for the same reason in Core/Hook/InlineSearch/KeyboardUtils.cs.
    // CharSet.Unicode is required here, not cosmetic: without it, this binds to SendMessageTimeoutA,
    // which sends WM_GETTEXT/WM_GETTEXTLENGTH through the ANSI thunk. That returns an ANSI byte count
    // for the length call and writes ANSI bytes for the text call, while SafeGetWindowText below
    // allocates a UTF-16 buffer and decodes it with PtrToStringUni -- the encoding mismatch corrupts
    // any title outside 7-bit ASCII into mojibake.
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, uint flags, uint timeoutMs, out IntPtr result);

    private const uint WM_GETTEXTLENGTH = 0x000E;
    private const uint WM_GETTEXT = 0x000D;
    private const uint SMTO_ABORTIFHUNG = 0x0002;
    private const uint GetTextTimeoutMs = 150;

    private static int SafeGetWindowTextLength(IntPtr hWnd) =>
        SendMessageTimeout(hWnd, WM_GETTEXTLENGTH, IntPtr.Zero, IntPtr.Zero, SMTO_ABORTIFHUNG, GetTextTimeoutMs, out var result) == IntPtr.Zero
            ? 0
            : result.ToInt32();

    private static string SafeGetWindowText(IntPtr hWnd, int titleLength)
    {
        var capacity = titleLength + 1;
        var buffer = Marshal.AllocHGlobal(capacity * sizeof(char));
        try
        {
            if (SendMessageTimeout(hWnd, WM_GETTEXT, new IntPtr(capacity), buffer, SMTO_ABORTIFHUNG, GetTextTimeoutMs, out var result) == IntPtr.Zero)
                return string.Empty;
            return Marshal.PtrToStringUni(buffer, result.ToInt32()) ?? string.Empty;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);

    private const uint GW_OWNER = 4;
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_APPWINDOW = 0x00040000;
    private const int DWMWA_CLOAKED = 14;

    // The same combination the shell's own Alt+Tab switcher and every third-party task-switcher use:
    // visible, no owner (excludes most dialogs/tool popups, which are owned by their parent), not
    // cloaked (excludes UWP windows minimized to another virtual desktop, which report as visible even
    // though there's nothing to switch to), a real title, and not a tool window unless it also opts
    // back in via WS_EX_APPWINDOW (some apps set both).
    internal static bool IsAltTabEligible(bool isVisible, bool hasOwner, bool isCloaked, int titleLength, bool isToolWindow, bool isAppWindow)
    {
        if (!isVisible) return false;
        if (hasOwner) return false;
        if (isCloaked) return false;
        if (titleLength == 0) return false;
        if (isToolWindow && !isAppWindow) return false;
        return true;
    }

    // Excludes Lertaro's own windows (search window, settings, ...) -- switching to the window you
    // just picked this result from would be meaningless, and it's about to hide anyway.
    public static List<SwitchableWindow> GetSwitchableWindows()
    {
        var currentProcessId = Environment.ProcessId;
        var results = new List<SwitchableWindow>();

        EnumWindows((hWnd, _) =>
        {
            try
            {
                var isVisible = IsWindowVisible(hWnd);
                var hasOwner = GetWindow(hWnd, GW_OWNER) != IntPtr.Zero;
                var exStyle = GetWindowLong(hWnd, GWL_EXSTYLE);
                var isToolWindow = (exStyle & WS_EX_TOOLWINDOW) != 0;
                var isAppWindow = (exStyle & WS_EX_APPWINDOW) != 0;

                var isCloaked = false;
                try
                {
                    if (DwmGetWindowAttribute(hWnd, DWMWA_CLOAKED, out var cloakedValue, sizeof(int)) == 0)
                        isCloaked = cloakedValue != 0;
                }
                catch { /* DWM unavailable -- treat as not cloaked */ }

                // EnumWindows visits every top-level window on the desktop -- often hundreds, mostly
                // invisible/owned helper windows -- and SendMessageTimeout's cross-thread dispatch has
                // real per-call overhead even against a healthy window, unlike the plain GetWindowText
                // it replaced. Checking every other (cheap, in-process) eligibility condition first and
                // only then paying for a title query keeps that cost down to the handful of windows that
                // could actually end up in the list, instead of every window EnumWindows ever sees.
                if (!isVisible || hasOwner || isCloaked || (isToolWindow && !isAppWindow))
                    return true;

                var titleLength = SafeGetWindowTextLength(hWnd);
                if (!IsAltTabEligible(isVisible, hasOwner, isCloaked, titleLength, isToolWindow, isAppWindow))
                    return true;

                GetWindowThreadProcessId(hWnd, out var pid);
                if (pid == currentProcessId)
                    return true;

                var title = SafeGetWindowText(hWnd, titleLength);

                results.Add(new SwitchableWindow(hWnd, title, (int)pid));
            }
            catch { /* skip a window that fails any of the above */ }

            return true;
        }, IntPtr.Zero);

        return results;
    }
}
