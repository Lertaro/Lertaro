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
        // 5702 was HideOrShow, removed on purpose: SW_HIDE takes the window out of this switcher's own
        // window list (and out of Alt+Tab), and the only way to undo it was this menu -- which can no
        // longer be reached for a window that is gone. The id is left unused rather than reused so the
        // remaining ids stay stable.
        Maximize = 5703,
        Minimize = 5704,
        Restore = 5705,
        Close = 5706,
        Focus = 5707
    }

    /// <summary>
    /// One menu row: which command it runs, which translation key labels it, whether it is a no-op right
    /// now, and the letter that activates it while the menu is open (the host matches a typed letter
    /// against this, so the letter is shown next to the row as well as being a trigger).
    /// </summary>
    internal readonly record struct WindowMenuEntry(uint CommandId, string LabelKey, bool Enabled, string ShortcutHint);

    /// <summary>
    /// Everything the menu decision depends on, read from the live window once per menu open.
    /// <see cref="IsValid"/> is false when the window is already gone (a stale instant result).
    /// </summary>
    internal readonly record struct WindowMenuState(bool IsValid, bool IsTopmost, bool IsVisible, bool IsMaximized, bool IsMinimized);

    // ===== Pure decision =====

    /// <summary>
    /// Maps the target window's current state to the menu rows to show. Entries that would be a no-op
    /// right now are still listed (so the menu's shape doesn't jump around) but disabled: Maximize when
    /// already maximized, Minimize when already minimized, Restore when neither. The topmost row's label
    /// flips with the state it describes.
    /// </summary>
    internal static IReadOnlyList<WindowMenuEntry> Build(WindowMenuState state)
    {
        var topmostKey = state.IsTopmost ? "WindowSwitcher_MenuCancelAlwaysOnTop" : "WindowSwitcher_MenuAlwaysOnTop";
        // A window cannot be both maximized and minimized, so this is exactly "restoring would change
        // something": a normal window has nothing to restore.
        var canRestore = state.IsMaximized || state.IsMinimized;

        return new[]
        {
            Entry(MenuCommand.ToggleTopmost, topmostKey, true, 'p'),
            Entry(MenuCommand.Maximize, "WindowSwitcher_MenuMaximize", !state.IsMaximized, 'm'),
            Entry(MenuCommand.Minimize, "WindowSwitcher_MenuMinimize", !state.IsMinimized, 'n'),
            Entry(MenuCommand.Restore, "WindowSwitcher_MenuRestore", canRestore, 'r'),
            Entry(MenuCommand.Close, "WindowSwitcher_MenuClose", true, 'c'),
            Entry(MenuCommand.Focus, "WindowSwitcher_MenuFocus", true, 'f')
        };
    }

    // Small helper so Build above stays a readable table.
    private static WindowMenuEntry Entry(MenuCommand command, string labelKey, bool enabled, char shortcut) =>
        new((uint)command, labelKey, enabled, shortcut.ToString());

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
    /// <remarks>
    /// SWP_NOACTIVATE is load-bearing, and SetWindowPos's success return does NOT reveal when it is
    /// missing: without it the call also asks to activate the target, and a window that is not the
    /// foreground one drops the whole positioning request while still returning TRUE. Measured on a real
    /// Explorer frame (CabinetWClass): NOSIZE|NOMOVE returns true and WS_EX_TOPMOST never appears;
    /// adding NOACTIVATE sets it and it stays set. A plain Notepad window happens to accept both, which
    /// is how this shipped wrong -- so this is about the target's tolerance, not about which window.
    /// </remarks>
    internal static bool SetTopmost(IntPtr hwnd, bool topmost)
    {
        if (!IsWindow(hwnd)) return false;
        return SetWindowPos(hwnd, topmost ? HwndTopmost : HwndNotopmost, 0, 0, 0, 0, SwpNoSize | SwpNoMove | SwpNoActivate);
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
    private const uint SwpNoActivate = 0x0010;

    private const int SwMaximize = 3;
    private const int SwMinimize = 6;
    private const int SwRestore = 9;

    private const uint WmClose = 0x0010;
}
