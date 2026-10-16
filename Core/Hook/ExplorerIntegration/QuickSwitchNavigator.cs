using Lertaro.Core.Hook.Commands;

namespace Lertaro.Core.Hook;

/// <summary>
/// What Ctrl+G does once the gesture is recognized: read the folder Quick Switch should land on from the
/// tracker's own state, and hand the navigation to the dialog's adapter.
/// </summary>
/// <remarks>
/// Both halves had to leave the low-level keyboard hook. The decision needs the tracker lock, which the
/// WinEvent thread holds across cross-process reads -- a hook that waits on it drops the keystroke, and one
/// that overruns LowLevelHooksTimeout is silently removed by Windows. The navigation calls into another
/// process. So the hook posts a thread message to the tracker thread and this runs there, against the state
/// that thread owns; <see cref="GlobalHotkeyDetector"/> keeps only the gesture.
/// </remarks>
internal static class QuickSwitchNavigator
{
    /// <summary>
    /// Runs one Ctrl+G on the tracker thread. Returns whether a navigation was handed off; every rejection is
    /// logged with its reason, because the user's only other evidence is that nothing happened.
    /// </summary>
    /// <param name="navigate">
    /// Test seam only: production hands the navigation to <see cref="FileDialogCommandHandler.NavigateOffThread"/>,
    /// which resolves the adapter for the window itself rather than reusing this snapshot.
    /// </param>
    internal static bool Execute(ExplorerTracker tracker, IntPtr dialogHwnd,
        Action<ExplorerTracker, IntPtr, string>? navigate = null)
    {
        string? path;
        lock (tracker.StateLock)
        {
            // Re-read rather than trust what the hook saw: this runs a moment later, on the thread that keeps
            // the state current, and the dialog may be gone or another window may have taken over since.
            // No adapter check here: whether one matches this window is the navigation's own question, and
            // asking it from the handle is the only answer that is not a snapshot (see ResolveAdapter).
            if (dialogHwnd == IntPtr.Zero || tracker.ActiveHwnd != dialogHwnd || !tracker.IsActiveWindowDialog)
                return Reject(dialogHwnd, "the tracked window is no longer the dialog");
            path = tracker.SelectQuickSwitchPath(dialogHwnd);
        }

        if (string.IsNullOrEmpty(path) || !Path.IsPathRooted(path))
            return Reject(dialogHwnd, $"no browsed folder is known yet (last='{path}')");
        if (!tracker.WindowIsAlive(dialogHwnd))
            return Reject(dialogHwnd, "the dialog window is gone");

        var navPath = path.EndsWith('\\') ? path : path + "\\";
        if (navigate == null) FileDialogCommandHandler.NavigateOffThread(tracker, dialogHwnd, navPath);
        else navigate(tracker, dialogHwnd, navPath);
        Logger.Log($"[QuickSwitch] Navigating 0x{dialogHwnd:x} to {navPath}.", LogLevel.Debug);
        return true;
    }

    private static bool Reject(IntPtr dialogHwnd, string reason)
    {
        Logger.Log($"[QuickSwitch] Not navigating 0x{dialogHwnd:x}: {reason}.", LogLevel.Info);
        return false;
    }
}
