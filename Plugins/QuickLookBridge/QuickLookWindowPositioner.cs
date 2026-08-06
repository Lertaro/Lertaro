using System.Diagnostics;
using System.Runtime.InteropServices;
using Lertaro.PluginSdk;

namespace Lertaro.Plugins.QuickLookBridge;

// Moves QuickLook's own top-level window to a target rectangle via SetWindowPos -- no re-parenting, no
// style changes, so QuickLook's own window stays a completely normal top-level window as far as it (or
// Windows) knows. The only reason this needs to poll at all is that Invoke over the pipe is fire-and-
// forget: there's no signal for "the window now exists/updated," so DockTo just keeps checking for up to
// ~3s after being asked to reposition. QuickLook's own layout code re-centers the window on its own
// schedule too (e.g. right after it finishes rendering new content) -- expect it to occasionally win that
// race and leave the window slightly out of place until the next navigation re-asserts the dock.
internal static class QuickLookWindowPositioner
{
    private const int PollIntervalMs = 75;
    private const int MaxPollAttempts = 40; // ~3s

    private static readonly object Lock = new();
    private static IntPtr _lastKnownHwnd = IntPtr.Zero;
    private static Timer? _pollTimer;
    private static (int Left, int Top, int Width, int Height) _target;

    // Called synchronously from the UI thread (owner LocationChanged/SizeChanged, or right after
    // SetTarget resolves a new file) -- must never itself block there. IsWindow() is a cheap table
    // lookup, safe to call inline, but SetWindowPos can send synchronous WM_WINDOWPOSCHANGING/CHANGED to
    // the target window and wait for its thread to process them; if QuickLook's own UI thread is busy
    // (e.g. still decoding the previous video file), that wait would show up here as the SAME kind of UI
    // stutter the pipe calls used to cause before those were made async. So the actual repositioning
    // always happens on a background thread, never on this one.
    public static void DockTo(int left, int top, int width, int height)
    {
        IntPtr hwndToReposition;

        lock (Lock)
        {
            _target = (left, top, width, height);

            if (_lastKnownHwnd != IntPtr.Zero && QuickLookDockInterop.IsWindow(_lastKnownHwnd))
            {
                hwndToReposition = _lastKnownHwnd;
            }
            else
            {
                hwndToReposition = IntPtr.Zero;

                // Update the target above, but DON'T restart a poll that's already running: DockTo gets
                // called on every owner LocationChanged/SizeChanged, and the owner's results list keeps
                // resizing while the user is still typing -- letting each of those calls dispose and
                // recreate the timer meant the poll's attempt counter kept getting reset back to 0 before
                // it ever ran long enough to actually find the window, so it silently never succeeded
                // (worst right after the first preview, exactly when the results list is still settling).
                // One poll per "don't have a window yet" episode always reads the latest _target when it
                // does find one.
                if (_pollTimer == null)
                {
                    Logger.Log($"[QuickLookBridge] dock poll starting, target=({left},{top},{width},{height})", LogLevel.Debug);
                    StartPollLocked();
                }
            }
        }

        if (hwndToReposition != IntPtr.Zero)
            Task.Run(() => { lock (Lock) { Reposition(hwndToReposition); } });
    }

    // Called by QuickLookPipeClient right before it sends a Close message (guaranteed to destroy and
    // recreate QuickLook's window, confirmed against its own source -- see CloseMessage's comment) and
    // whenever the pipe availability check flips from reachable to unreachable (the process itself likely
    // exited). Both are cases where the cached hwnd is either certainly or very likely stale, and Windows
    // can recycle HWND values once one's destroyed, so IsWindow() alone can't always tell "still the same
    // window" from "a different window that happens to share the old number."
    public static void Reset()
    {
        lock (Lock)
        {
            _lastKnownHwnd = IntPtr.Zero;
            if (_pollTimer != null)
            {
                // Otherwise an in-flight poll just silently vanishes from the log with neither a
                // "succeeded" nor a "gave up" line -- confirmed happening (against an older build,
                // before this line existed) by a ~5s gap between "dock poll starting" and the next
                // QuickLookBridge log entry, for an entirely different file.
                Logger.Log("[QuickLookBridge] Reset() aborted an in-flight dock poll", LogLevel.Debug);
                _pollTimer.Dispose();
                _pollTimer = null;
            }
        }
    }

    // Caller already holds Lock.
    private static void StartPollLocked()
    {
        _pollTimer?.Dispose();
        var attempts = 0;
        _pollTimer = new Timer(_ =>
        {
            attempts++;
            var pid = GetQuickLookProcessId();
            var hwnd = pid != 0 ? FindTopLevelWindow(pid) : IntPtr.Zero;

            if (hwnd != IntPtr.Zero)
            {
                lock (Lock)
                {
                    _lastKnownHwnd = hwnd;
                    Reposition(hwnd);
                    _pollTimer?.Dispose();
                    _pollTimer = null;
                }
                Logger.Log($"[QuickLookBridge] dock poll succeeded after {attempts} attempt(s)", LogLevel.Debug);
                ScheduleSettleReasserts(hwnd);
                return;
            }

            if (attempts >= MaxPollAttempts)
            {
                Logger.Log("[QuickLookBridge] dock poll gave up: no QuickLook window found in time", LogLevel.Warn);
                lock (Lock)
                {
                    _pollTimer?.Dispose();
                    _pollTimer = null;
                }
            }
        }, null, PollIntervalMs, PollIntervalMs);
    }

    // QuickLook's own layout code (centering/resizing for the new content) can run asynchronously a
    // moment after the window we just docked first becomes findable -- e.g. it finishes decoding/laying
    // out the file after DoPreview() returns -- and silently overwrite the position/size we just set,
    // which is what "occasionally changes size" looks like from the outside. A couple of short follow-up
    // re-asserts gives our position a much better chance of being the one that's still in effect once
    // QuickLook's own pass has actually settled, without polling indefinitely. Uses Task.Delay rather
    // than a bare Timer -- an unreferenced System.Threading.Timer can be garbage-collected before its
    // callback ever fires, since nothing else roots it.
    private static void ScheduleSettleReasserts(IntPtr hwnd)
    {
        foreach (var delayMs in SettleReassertDelaysMs)
        {
            _ = Task.Delay(delayMs).ContinueWith(_ =>
            {
                lock (Lock)
                {
                    if (_lastKnownHwnd == hwnd && QuickLookDockInterop.IsWindow(hwnd))
                        Reposition(hwnd);
                }
            }, TaskScheduler.Default);
        }
    }

    private static readonly int[] SettleReassertDelaysMs = { 150, 400, 900 };

    // Caller already holds Lock.
    private static void Reposition(IntPtr hwnd)
    {
        try
        {
            var ok = QuickLookDockInterop.SetWindowPos(hwnd, IntPtr.Zero, _target.Left, _target.Top, _target.Width, _target.Height,
                QuickLookDockInterop.SWP_NOZORDER | QuickLookDockInterop.SWP_NOACTIVATE);
            if (!ok)
            {
                var err = Marshal.GetLastWin32Error();
                Logger.Log($"[QuickLookBridge] SetWindowPos FAILED, target=({_target.Left},{_target.Top},{_target.Width},{_target.Height}) Win32Error={err}", LogLevel.Warn);
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"[QuickLookBridge] Reposition threw: {ex.GetType().Name}: {ex.Message}", LogLevel.Warn);
        }
    }

    private static int GetQuickLookProcessId()
    {
        var processes = Process.GetProcessesByName("QuickLook");
        try
        {
            return processes.Length > 0 ? processes[0].Id : 0;
        }
        finally
        {
            foreach (var process in processes) process.Dispose();
        }
    }

    private static IntPtr FindTopLevelWindow(int processId)
    {
        var found = IntPtr.Zero;
        QuickLookDockInterop.EnumWindows((hwnd, _) =>
        {
            QuickLookDockInterop.GetWindowThreadProcessId(hwnd, out var pid);
            if (pid != processId || !QuickLookDockInterop.IsWindowVisible(hwnd))
                return true; // keep enumerating

            QuickLookDockInterop.GetWindowRect(hwnd, out var rect);
            if (rect.Right - rect.Left < 50 || rect.Bottom - rect.Top < 50)
                return true; // too small to be the viewer window (tray helper, tooltip, ...)

            found = hwnd;
            return false; // stop enumerating
        }, IntPtr.Zero);
        return found;
    }
}
