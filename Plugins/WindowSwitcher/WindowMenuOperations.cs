using System.Runtime.InteropServices;

namespace Lertaro.Plugins.WindowSwitcher;

// The window operations behind the "Window" actions menu, plus the two pure pieces the tests pin:
// Build (state -> menu entries) and ParseWindowHandle (action argument -> HWND). Keeping the decision
// pure is the whole point -- the P/Invoke half below can only be exercised against real windows, so it
// is deliberately as thin as possible: every method is one call preceded by an IsWindow guard.
internal static class WindowMenuOperations
{
    /// <summary>Command ids for this provider's menu entries. Stable, small, and all in one place.</summary>
    internal enum MenuCommand
    {
        ToggleTopmost = 5701,
        HideOrShow = 5702,
        Maximize = 5703,
        Minimize = 5704,
        Restore = 5705,
        Close = 5706,
        Focus = 5707
    }

    /// <summary>One menu row: which command it runs and which translation key labels it.</summary>
    internal readonly record struct WindowMenuEntry(uint CommandId, string LabelKey, bool Enabled);

    /// <summary>
    /// Everything the menu decision depends on, read from the live window once per menu open.
    /// <see cref="IsValid"/> is false when the window is already gone (a stale instant result).
    /// </summary>
    internal readonly record struct WindowMenuState(bool IsValid, bool IsTopmost, bool IsVisible, bool IsMaximized, bool IsMinimized);

    // ===== Pure decision =====

    /// <summary>
    /// Maps the target window's current state to the menu rows to show. Entries that would be a no-op
    /// right now are still listed (so the menu's shape doesn't jump around) but disabled: Maximize when
    /// already maximized, Minimize when already minimized, Restore when neither. The label of the
    /// topmost and hide/show rows flips with the state they describe.
    /// </summary>
    internal static IReadOnlyList<WindowMenuEntry> Build(WindowMenuState state)
    {
        var topmostKey = state.IsTopmost ? "WindowSwitcher_MenuCancelAlwaysOnTop" : "WindowSwitcher_MenuAlwaysOnTop";
        var visibilityKey = state.IsVisible ? "WindowSwitcher_MenuHide" : "WindowSwitcher_MenuShow";
        // A window cannot be both maximized and minimized, so this is exactly "restoring would change
        // something": a normal window has nothing to restore.
        var canRestore = state.IsMaximized || state.IsMinimized;

        return new[]
        {
            Entry(MenuCommand.ToggleTopmost, topmostKey, true),
            Entry(MenuCommand.HideOrShow, visibilityKey, true),
            Entry(MenuCommand.Maximize, "WindowSwitcher_MenuMaximize", !state.IsMaximized),
            Entry(MenuCommand.Minimize, "WindowSwitcher_MenuMinimize", !state.IsMinimized),
            Entry(MenuCommand.Restore, "WindowSwitcher_MenuRestore", canRestore),
            Entry(MenuCommand.Close, "WindowSwitcher_MenuClose", true),
            Entry(MenuCommand.Focus, "WindowSwitcher_MenuFocus", true)
        };
    }

    // Small helper so Build above stays a readable table.
    private static WindowMenuEntry Entry(MenuCommand command, string labelKey, bool enabled) =>
        new((uint)command, labelKey, enabled);

    /// <summary>
    /// Extracts the target HWND from an instant result's action argument. Returns <see cref="IntPtr.Zero"/>
    /// for anything that is not an <c>activatewindow:&lt;hwnd&gt;</c> argument -- every other result
    /// kind, a malformed value, or the "0" window handle (which is never a real target).
    /// </summary>
    internal static IntPtr ParseWindowHandle(string? actionArgument)
    {
        if (string.IsNullOrEmpty(actionArgument)) return IntPtr.Zero;
        if (!actionArgument.StartsWith(ActivateWindowPrefix, StringComparison.OrdinalIgnoreCase)) return IntPtr.Zero;

        var raw = actionArgument.Substring(ActivateWindowPrefix.Length).Trim();
        // Parse as Int64 (and reject zero/negative) so a 64-bit HWND survives on both bitnesses.
        return long.TryParse(raw, out var value) && value > 0 ? new IntPtr(value) : IntPtr.Zero;
    }

    // ===== Live state =====

    /// <summary>
    /// Reads the target window's current state. The single place the Win32 queries live, which is what
    /// keeps <see cref="Build"/> pure and testable.
    /// </summary>
    internal static WindowMenuState ReadState(IntPtr hwnd)
    {
        if (!IsWindow(hwnd)) return default;

        return new WindowMenuState(
            IsValid: true,
            IsTopmost: IsTopmost(hwnd),
            IsVisible: IsWindowVisible(hwnd),
            IsMaximized: IsZoomed(hwnd),
            IsMinimized: IsIconic(hwnd));
    }

    // ===== Window operations =====
    // Every one of these re-checks IsWindow first: a result stays in the list while the window behind
    // it may close at any moment, and menu execution must fail quietly rather than throw a stale handle
    // into the user's face.

    /// <summary>Adds or removes WS_EX_TOPMOST, the same thing a titlebar "Always on top" toggle does.</summary>
    internal static bool SetTopmost(IntPtr hwnd, bool topmost)
    {
        if (!IsWindow(hwnd)) return false;
        return SetWindowPos(hwnd, topmost ? HwndTopmost : HwndNotopmost, 0, 0, 0, 0, SwpNoSize | SwpNoMove);
    }

    /// <summary>Hides or shows the window (SW_HIDE / SW_SHOW), leaving its place on the taskbar alone.</summary>
    internal static bool SetVisible(IntPtr hwnd, bool visible)
    {
        if (!IsWindow(hwnd)) return false;
        ShowWindow(hwnd, visible ? SwShow : SwHide);
        return true;
    }

    internal static bool Maximize(IntPtr hwnd)
    {
        if (!IsWindow(hwnd)) return false;
        ShowWindow(hwnd, SwMaximize);
        return true;
    }

    internal static bool Minimize(IntPtr hwnd)
    {
        if (!IsWindow(hwnd)) return false;
        ShowWindow(hwnd, SwMinimize);
        return true;
    }

    internal static bool Restore(IntPtr hwnd)
    {
        if (!IsWindow(hwnd)) return false;
        ShowWindow(hwnd, SwRestore);
        return true;
    }

    /// <summary>
    /// Asks the window to close with WM_CLOSE -- posted, not sent, so a window that decides to show a
    /// "save your work?" prompt does not block this process while the user answers it.
    /// </summary>
    internal static bool Close(IntPtr hwnd)
    {
        if (!IsWindow(hwnd)) return false;
        return PostMessage(hwnd, WmClose, IntPtr.Zero, IntPtr.Zero);
    }

    /// <summary>
    /// Brings the target window to the foreground, restoring it first when it is minimized.
    /// ponytail: this provider runs inside the Lertaro App process, which still owns the foreground
    /// window at the moment a menu action fires, so a plain SetForegroundWindow is permitted here (the
    /// host hides its own search window when the action runs and has its own foreground-restore path of
    /// its own). Ceiling: if focus is ever stolen back by that restore path, the upgrade is to route
    /// this through the host's existing "activatewindow:" handling in
    /// App/Services/Plugin/PluginActionExecutor.cs, which calls QuickSearchWindowController.ForceForeground
    /// and suppresses the restore.
    /// </summary>
    internal static bool Focus(IntPtr hwnd)
    {
        if (!IsWindow(hwnd)) return false;
        if (IsIconic(hwnd)) ShowWindow(hwnd, SwRestore);
        return SetForegroundWindow(hwnd);
    }

    // ===== P/Invoke =====

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsZoomed(IntPtr hWnd);

    // GetWindowLongPtrW only exists on 64-bit Windows; on 32-bit the export is GetWindowLongW and the
    // pointer-sized style fits in a 32-bit int. Netting the two to one IntPtr return keeps the caller
    // expression below free of any bitness check.
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    private static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex) =>
        IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, nIndex) : GetWindowLong32(hWnd, nIndex);

    private static bool IsTopmost(IntPtr hWnd) =>
        (GetWindowLongPtr(hWnd, GwlExStyle).ToInt64() & WsExTopmost) != 0;

    private const string ActivateWindowPrefix = "activatewindow:";

    private const int GwlExStyle = -20;
    private const long WsExTopmost = 0x00000008L;

    // HWND_TOPMOST/HWND_NOTOPMOST are the pseudo-handles (IntPtr)(-1) and (IntPtr)(-2).
    private static readonly IntPtr HwndTopmost = new(-1);
    private static readonly IntPtr HwndNotopmost = new(-2);

    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;

    private const int SwHide = 0;
    private const int SwMaximize = 3;
    private const int SwShow = 5;
    private const int SwMinimize = 6;
    private const int SwRestore = 9;

    private const uint WmClose = 0x0010;
}
