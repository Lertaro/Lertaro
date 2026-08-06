using System.Runtime.InteropServices;
using System.Windows.Threading;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.CoreExtensions.Preview.Handlers;

// Recovery layer for a native preview handler's own out-of-process window -- e.g. Excel, running as its
// own prevhost surrogate rather than a thin preview-only handler -- stealing OS keyboard focus away from
// the host app once its content finishes initializing. PreviewHandlerHost.WndProc handles prevention
// (WM_MOUSEACTIVATE/WM_SETFOCUS on _hostHwnd itself); this class is the fallback for whatever slips past
// that, e.g. an asynchronous, non-click-driven SetFocus a handler issues on its own schedule.
//
// Tried and reverted here: severing the child's input-queue attachment via AttachThreadInput(..., false)
// on WM_PARENTNOTIFY, hoping to stop the steal before it happens. It backfired -- the child's implicit
// attachment to our queue is apparently what keeps its clicks from triggering a REAL OS-level foreground
// switch to its own top-level window; detaching it made clicking into the preview activate that window
// for real, which cascaded into QuickLookManager.Owner_Deactivated treating it as focus lost to another
// app entirely and closing the whole search window.
//
// A bounded, PID-scoped EVENT_OBJECT_FOCUS watcher: reacts to the real focus-change event (not a guessed
// delay) and reports back via PreviewActivationSignal so the host can reclaim focus.
internal sealed class PreviewFocusGuard
{
    private delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

    [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

    private const uint EVENT_OBJECT_FOCUS = 0x8005;
    // Fires specifically when a dialog box is about to be shown -- e.g. Word's own "Enter password"
    // prompt for an encrypted file it's asked to preview. Far more precise than inferring one from generic
    // window-creation traffic, and (unlike EVENT_OBJECT_FOCUS below) not scoped to a short post-load grace
    // window: a handler can show one of these at any point in its session, not just right after cold start
    // (e.g. also while the user is actively interacting with a live Excel/Word preview).
    private const uint EVENT_SYSTEM_DIALOGSTART = 0x0016;
    // Fires when any window (not just dialogs) is destroyed -- scoped to the handler's own PID and
    // further filtered to the exact tracked dialog hwnd in the callback below, so an unrelated window
    // closing in that same process doesn't falsely signal "the dialog is gone".
    private const uint EVENT_OBJECT_DESTROY = 0x8001;
    private const uint WINEVENT_OUTOFCONTEXT = 0;

    private WinEventDelegate? _focusHookDelegate;
    private IntPtr _hFocusHook;
    private DispatcherTimer? _focusGraceTimer;

    private WinEventDelegate? _dialogHookDelegate;
    private IntPtr _hDialogHook;

    private WinEventDelegate? _dialogCloseHookDelegate;
    private IntPtr _hDialogCloseHook;
    private IntPtr _trackedDialogHwnd;

    // Called on WM_PARENTNOTIFY(WM_CREATE) for the host window -- the earliest reliable signal that the
    // handler's rendering window (possibly cross-process) has been attached as our child, so its PID is
    // known before GrantForegroundRights (which runs later, after DoPreview returns) would otherwise
    // resolve it.
    public void OnChildWindowCreated(IntPtr childHwnd)
    {
        if (childHwnd == IntPtr.Zero) return;
        try
        {
            PreviewHandlerInterop.GetWindowThreadProcessId(childHwnd, out var pid);
            if (pid == 0) return;
            ArmFallbackDetector(pid);
            ArmDialogWatcher(pid);
        }
        catch { }
    }

    // A handler's own popup (a password prompt, a "file in use" notice, ...) is a real top-level window of
    // its process, not reparented under our host -- it never gets the OS foreground/focus it would in a
    // normal standalone launch, since the process hosting it isn't the one the user is actively working in.
    // Left alone, it just sits there invisible behind everything else, and the whole app looks hung waiting
    // on it (issue #133) rather than showing what's actually blocking. This brings it to the front the
    // instant it appears instead. Scoped to the handler's own PID so an unrelated app's dialog is never
    // touched, and armed for the guard's whole lifetime (not the shorter focus-steal grace window) since a
    // dialog can appear at any point in the session, not just right after load. The quick window's own
    // foreground-loss hide already tolerates this process holding focus for as long as this preview host is
    // alive (see PreviewActivationSignal), so bringing the dialog forward doesn't risk it closing itself.
    private void ArmDialogWatcher(uint pid)
    {
        DisarmDialogWatcher();
        _dialogHookDelegate = (h, evt, hwnd, idObject, idChild, thread, time) =>
        {
            if (hwnd == IntPtr.Zero) return;
            PreviewHandlerInterop.GetWindowThreadProcessId(hwnd, out var dialogPid);
            if (dialogPid != pid) return;
            try { PreviewHandlerInterop.SetForegroundWindow(hwnd); } catch { }
            PreviewDialogSignal.NotifyDialogOpened();
            ArmDialogCloseWatcher(hwnd, pid);
        };
        _hDialogHook = SetWinEventHook(EVENT_SYSTEM_DIALOGSTART, EVENT_SYSTEM_DIALOGSTART, IntPtr.Zero, _dialogHookDelegate, 0, 0, WINEVENT_OUTOFCONTEXT);
    }

    private void DisarmDialogWatcher()
    {
        if (_hDialogHook != IntPtr.Zero)
        {
            UnhookWinEvent(_hDialogHook);
            _hDialogHook = IntPtr.Zero;
        }
        _dialogHookDelegate = null;
    }

    // Tracks the specific dialog hwnd SetForegroundWindow was just called on, so the app's own
    // hidden-for-dialog windows come back the moment THAT window closes, not some unrelated one.
    private void ArmDialogCloseWatcher(IntPtr dialogHwnd, uint pid)
    {
        DisarmDialogCloseWatcher();
        _trackedDialogHwnd = dialogHwnd;
        _dialogCloseHookDelegate = (h, evt, hwnd, idObject, idChild, thread, time) =>
        {
            if (hwnd != _trackedDialogHwnd) return;
            DisarmDialogCloseWatcher();
            PreviewDialogSignal.NotifyDialogClosed();
        };
        _hDialogCloseHook = SetWinEventHook(EVENT_OBJECT_DESTROY, EVENT_OBJECT_DESTROY, IntPtr.Zero, _dialogCloseHookDelegate, pid, 0, WINEVENT_OUTOFCONTEXT);
    }

    private void DisarmDialogCloseWatcher()
    {
        if (_hDialogCloseHook != IntPtr.Zero)
        {
            UnhookWinEvent(_hDialogCloseHook);
            _hDialogCloseHook = IntPtr.Zero;
        }
        _dialogCloseHookDelegate = null;
        _trackedDialogHwnd = IntPtr.Zero;
    }

    // Some handlers (Excel especially) can still grab focus asynchronously, on their own schedule --
    // anywhere from tens of milliseconds to well over a second later depending on the file -- despite
    // PreviewHandlerHost's WM_MOUSEACTIVATE/WM_SETFOCUS prevention. Reacting to the real focus-change
    // event instead of guessing a fixed delay catches it exactly when it happens, whatever that timing
    // turns out to be. Scoped to the specific handler PID (an unrelated app the user switches to isn't
    // mistaken for a steal) and to a bounded grace window after load (so a later, deliberate click into
    // the preview -- scrolling, playback controls -- is left alone instead of being fought).
    private void ArmFallbackDetector(uint pid)
    {
        DisarmFallbackDetector();
        _focusHookDelegate = (h, evt, hwnd, idObject, idChild, thread, time) =>
        {
            if (hwnd == IntPtr.Zero) return;
            PreviewHandlerInterop.GetWindowThreadProcessId(hwnd, out var focusedPid);
            if (focusedPid != pid) return;
            DisarmFallbackDetector();
            PreviewActivationSignal.NotifyFocusStolen();
        };
        _hFocusHook = SetWinEventHook(EVENT_OBJECT_FOCUS, EVENT_OBJECT_FOCUS, IntPtr.Zero, _focusHookDelegate, 0, 0, WINEVENT_OUTOFCONTEXT);

        _focusGraceTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _focusGraceTimer.Tick += (s, e) => DisarmFallbackDetector();
        _focusGraceTimer.Start();
    }

    private void DisarmFallbackDetector()
    {
        if (_hFocusHook != IntPtr.Zero)
        {
            UnhookWinEvent(_hFocusHook);
            _hFocusHook = IntPtr.Zero;
        }
        _focusHookDelegate = null;
        _focusGraceTimer?.Stop();
        _focusGraceTimer = null;
    }

    public void Dispose()
    {
        DisarmFallbackDetector();
        DisarmDialogWatcher();

        // A dialog was still up (and the app's windows hidden for it) when this guard's own session
        // ended (e.g. the preview host itself was torn down) -- notify closed anyway so those windows
        // don't stay hidden forever with nothing left to ever signal their return.
        if (_hDialogCloseHook != IntPtr.Zero)
        {
            DisarmDialogCloseWatcher();
            PreviewDialogSignal.NotifyDialogClosed();
        }
    }
}
