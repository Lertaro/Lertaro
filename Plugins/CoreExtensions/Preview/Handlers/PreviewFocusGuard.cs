using System.Runtime.InteropServices;
using System.Windows.Interop;
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
    // Which PID the running start-watcher filters on, or AnyProcess while the handler's own PID is still
    // unknown. Null once disarmed.
    private uint? _dialogWatcherPid;

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
        _dialogWatcherPid = pid;
        _dialogHookDelegate = (h, evt, hwnd, idObject, idChild, thread, time) =>
        {
            if (hwnd == IntPtr.Zero) return;
            PreviewHandlerInterop.GetWindowThreadProcessId(hwnd, out var dialogPid);
            if (!ShouldSurfaceDialog(pid, dialogPid, IsOwnWpfWindow(hwnd))) return;
            SurfaceDialog(hwnd);
            PreviewDialogSignal.NotifyDialogOpened();
            ArmDialogCloseWatcher(hwnd, dialogPid);
        };
        _hDialogHook = SetWinEventHook(EVENT_SYSTEM_DIALOGSTART, EVENT_SYSTEM_DIALOGSTART, IntPtr.Zero, _dialogHookDelegate, 0, 0, WINEVENT_OUTOFCONTEXT);
    }

    /// <summary>PID marker for a start-watcher that has no PID to filter on yet.</summary>
    private const uint AnyProcess = 0;

    /// <summary>
    /// Arms the dialog watcher for one <c>Render</c> call, before the handler is handed the file.
    /// </summary>
    /// <remarks>
    /// A file the handler cannot parse -- an encrypted .doc that makes Word ask which encoding to import it
    /// as, issue #459 -- shows its dialog inside <c>Initialize</c>/<c>DoPreview</c> and never parents a render
    /// window, so <see cref="OnChildWindowCreated"/>, the only thing that used to arm this watcher, never
    /// fires and the prompt goes completely unseen. No PID is knowable that early, so this arm is unscoped;
    /// <see cref="ShouldSurfaceDialog"/> still refuses anything that is one of this app's own windows, and
    /// the arm is dropped when the render call ends, so an unrelated app's dialog cannot keep it alive past
    /// the one moment it could plausibly be confused for the handler's.
    /// </remarks>
    public void ArmDialogWatcherForRender() => ArmDialogWatcher(AnyProcess);

    public void DisarmDialogWatcherAfterRender()
    {
        // A render that got as far as parenting a window has already been re-armed for the handler's own PID
        // by OnChildWindowCreated. That arm belongs to the whole preview session and outlives this call.
        if (_dialogWatcherPid is null or AnyProcess)
            DisarmDialogWatcher();
    }

    // Which dialogs a running watcher reacts to. Once the handler's PID is known that is the only PID that
    // counts; before then any process does, because the in-process handlers are the interesting case -- their
    // dialog is this process's own window. Neither case may react to a window this app showed through WPF, or
    // opening the settings page or one of our message boxes would hide the search window out from under it.
    internal static bool ShouldSurfaceDialog(uint watcherPid, uint dialogPid, bool isOwnWpfWindow)
        => !isOwnWpfWindow && (watcherPid == AnyProcess || watcherPid == dialogPid);

    internal static bool IsOwnWpfWindow(IntPtr hwnd)
    {
        // HwndSource answers for the handles this process created through WPF only, so a foreign window --
        // and a native dialog an in-process handler pumps on this thread -- both come back null here.
        var root = PreviewHandlerInterop.GetAncestor(hwnd, PreviewHandlerInterop.GA_ROOT);
        return root != IntPtr.Zero && HwndSource.FromHwnd(root) is not null;
    }

    // Foreground alone is the worse outcome here: a window that is not topmost can never rise above the
    // topmost window this app previews inside, so the dialog ends up holding the keyboard while our window
    // keeps the pixels -- an app that takes no input and shows no reason why. Putting it in the same band is
    // what makes it both reachable and visible.
    // ponytail: the topmost style is never taken back, because the only close signal tracked here is the
    // window's destruction, by which point there is nothing left to un-topmost. A handler that hides a dialog
    // instead of closing it would leave it above everything; the upgrade is a restore on EVENT_OBJECT_HIDE.
    private static void SurfaceDialog(IntPtr hwnd)
    {
        try
        {
            PreviewHandlerInterop.SetWindowPos(hwnd, PreviewHandlerInterop.HWND_TOPMOST, 0, 0, 0, 0,
                PreviewHandlerInterop.SWP_NOMOVE | PreviewHandlerInterop.SWP_NOSIZE);
        }
        catch { }
        try { PreviewHandlerInterop.SetForegroundWindow(hwnd); } catch { }
    }

    private void DisarmDialogWatcher()
    {
        if (_hDialogHook != IntPtr.Zero)
        {
            UnhookWinEvent(_hDialogHook);
            _hDialogHook = IntPtr.Zero;
        }
        _dialogHookDelegate = null;
        _dialogWatcherPid = null;
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
