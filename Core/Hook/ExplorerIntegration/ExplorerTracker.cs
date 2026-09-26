using System.Text;
using Lertaro.PluginSdk.Registries;
using Lertaro.PluginSdk.Abstractions.Plugins.WindowAdapters;
namespace Lertaro.Core.Hook;

public class ExplorerTracker : IDisposable
{
    internal object StateLock { get; } = new();

    // The gap between re-asks of a dialog this process could not measure -- ExplorerActivePathPoller's own
    // quiet period, which is the gap its equivalent retry gets.
    private const int DialogRematchGapMs = 200;

    // The window a re-derivation chain is already running for. Cleared whenever the tracked window's adapters
    // are re-derived, because Windows hands out the same handle again and a stale mark would then suppress the
    // re-ask a genuinely new dialog needs.
    private IntPtr _rederivedFor;

    private ExplorerNativeHooks.WinEventDelegate? _winEventDelegate;
    private IntPtr _hForegroundHook = IntPtr.Zero;
    private IntPtr _hNameChangeHook = IntPtr.Zero;
    private IntPtr _hLocationChangeHook = IntPtr.Zero;
    private IntPtr _hFocusHook = IntPtr.Zero;
    private bool _isRunning;
    private readonly FileDialogNavigationTracker _dialogTracker = new();
    private readonly ExplorerWindowClassifier _classifier;
    private readonly ExplorerActivePathPoller _pathPoller;
    // Internal state exposed to ExplorerWindowClassifier
    public string? LastPath { get; set; }
    public Func<string, string?>? PathNormalizer { get; set; }
    public IntPtr LastActiveHwnd { get; set; }
    public string? LastActiveExplorerPath => _dialogTracker.LastActiveExplorerPath;
    public string? LastActiveExplorerClassName { get; set; }
    public string? LastActiveExplorerWindowTitle { get; set; }
    public bool IsExplorerOrDesktopActive { get; set; }
    public bool IsDesktop { get; set; }
    private bool _isActiveWindowDialog;
    public bool IsActiveWindowDialog { get => _isActiveWindowDialog; set => _isActiveWindowDialog = value; }
    public bool IsActiveWindowExplorer { get; set; }
    public IFileDialogAdapter? ActiveAdapter { get; private set; }
    public IInlineSearchAdapter? ActiveInlineAdapter { get; private set; }
    private IntPtr _activeHwnd;
    public IntPtr ActiveHwnd
    {
        get => _activeHwnd;
        set
        {
            _activeHwnd = value;
            RefreshActiveWindowAdapters();
        }
    }

    /// <summary>Re-evaluates the cached adapters after settings or component enablement changes.</summary>
    public void RefreshActiveWindowAdapters()
    {
        _rederivedFor = IntPtr.Zero;
        if (_activeHwnd == IntPtr.Zero)
        {
            ActiveAdapter = null;
            _isActiveWindowDialog = false;
            ActiveInlineAdapter = null;
            IsActiveWindowExplorer = false;
            return;
        }

        var sbClass = new StringBuilder(256);
        ExplorerNativeHooks.GetClassName(_activeHwnd, sbClass, sbClass.Capacity);
        var className = sbClass.ToString();
        var processName = GetProcessName(_activeHwnd);
        var budget = TimeSpan.FromMilliseconds(ExplorerWindowClassifier.DefaultPluginTimeoutMs);
        var hwnd = _activeHwnd;

        ActiveAdapter = MatchFileDialogAdapter(hwnd, className, processName, budget);
        _isActiveWindowDialog = ActiveAdapter != null;
        ActiveInlineAdapter = ExplorerStaInvoker.RunOnStaWithTimeout(
            () => InlineSearchAdapterRegistry.GetMatchingAdapter(hwnd, className, processName),
            (IInlineSearchAdapter?)null, budget);
        IsActiveWindowExplorer = !IsDesktop && (ActiveInlineAdapter?.IsFileExplorer ?? false);

        // Worth its line: which adapter WON is the answer every geometry and folder-scope question is asked
        // of, and nothing else said it out loud. Three reports about where the card landed, and the two
        // adapters that divide the common dialogs on a child window built after the dialog appeared, were
        // both invisible from outside the process.
        Logger.Log(
            $"[ExplorerTracker] Adapters for 0x{hwnd:x}: dialog={ActiveAdapter?.GetType().Name ?? "none"}, "
            + $"inline={ActiveInlineAdapter?.GetType().Name ?? "none"} (class={className}, process={processName})",
            LogLevel.Debug);
    }

    // Bounded, like every other plugin read in this file's callers. An adapter's CanHandle is a
    // cross-process probe for the dialogs whose widgets carry no window handles of their own, and for the
    // ones that walk child windows, and this runs on whichever thread just learned about a foreground
    // change -- in the App, the IPC mirror thread that carries *every* event. Measured on a Rimage folder
    // dialog: its thread was still busy initializing, this read parked behind it for seconds, and the whole
    // mirror went quiet rather than just this one window being answered wrong.
    private static IFileDialogAdapter? MatchFileDialogAdapter(IntPtr hwnd, string className, string processName, TimeSpan budget) =>
        ExplorerStaInvoker.RunOnStaWithTimeout(
            () => FileDialogAdapterRegistry.GetMatchingAdapter(hwnd, className, processName),
            (IFileDialogAdapter?)null, budget);

    /// <summary>
    /// Asks this process's adapters about the tracked dialog again, and adopts their answer if it has since
    /// become a different one.
    /// </summary>
    /// <remarks>
    /// The single read in <see cref="RefreshActiveWindowAdapters"/> happens in the instant an activation is
    /// mirrored, which is the instant a dialog is least able to answer for itself. For the common dialogs that
    /// is not merely a wrong answer, it is a DIFFERENT adapter, because two of them divide the field on a
    /// child window that has not been created yet: StandardFileDialogAdapter requires a "Breadcrumb Parent"
    /// and ClassicFileDialogAdapter -- registered ahead of it -- requires its ABSENCE, plus a file-name edit
    /// and a combo box. Both of those come from the dialog template and so exist from the first frame, while
    /// the breadcrumb is built later, so a modern dialog is claimed by the classic adapter until it settles.
    /// ClassicFileDialogAdapter has no file list to report, so the card was left with nothing to hang from
    /// and the folder-only scope that reads ActiveAdapter.TargetIsFolderOnly had nothing to read -- and
    /// because ActiveAdapter was not null, an absence check would have called this healthy.
    ///
    /// So the repair asks whether the registry's CURRENT answer differs from the one held, which is also what
    /// covers a read that simply timed out or matched nothing. Keyed on that state rather than on the path
    /// event that first reveals it, because more than one route reaches it and only one announces itself:
    /// measured on live Rimage dialogs, the tracker held an adapter it should not have for the whole life of
    /// the card without ever taking the claim branch, and only a focus change -- the next activation, hence
    /// the next RefreshActiveWindowAdapters -- ended it.
    ///
    /// Off the calling thread and speculative-tight on the read: callers include the IPC mirror thread and
    /// the geometry measurement, and parking the former is what commit 2acff94 was written to stop. The ask
    /// count and read budget are the hook process's own retry values, so both processes give the same dialog
    /// the same chance; the App mirrors state instead of polling for it, so its retry has to live here.
    ///
    /// ponytail: one bounded chain per dialog rather than a watcher, so a dialog that genuinely has no file
    /// list to report -- AutoCAD, Bandizip, WinRAR -- spends twelve cheap re-reads on it and then goes quiet.
    /// The ceiling is a dialog that finishes building only after that budget is spent; the next activation
    /// still catches it, and a card no longer visibly jumps when it does. Lifting the ceiling properly means
    /// the App polling for itself the way <see cref="ExplorerActivePathPoller"/> does, which it cannot do
    /// today because it mirrors the hook's state rather than reading events of its own.
    /// </remarks>
    public void RederiveActiveDialogAdapterIfStale()
    {
        IntPtr hwnd;
        lock (StateLock)
        {
            if (!_isActiveWindowDialog || _activeHwnd == IntPtr.Zero) return;
            hwnd = _activeHwnd;
            // One chain per window: the geometry probe asks on every measurement it takes, and a dialog whose
            // adapters are simply done -- AutoCAD, Bandizip, WinRAR -- must not spawn a chain per ask.
            if (_rederivedFor == hwnd) return;
            _rederivedFor = hwnd;
        }

        Task.Run(() =>
        {
            for (var ask = 1; ask <= ExplorerActivePathPoller.UnclaimedDialogRetryLimit; ask++)
            {
                if (ask > 1) Thread.Sleep(DialogRematchGapMs);

                IFileDialogAdapter? held;
                lock (StateLock)
                {
                    if (_activeHwnd != hwnd) return;
                    held = ActiveAdapter;
                }

                var sbClass = new StringBuilder(256);
                ExplorerNativeHooks.GetClassName(hwnd, sbClass, sbClass.Capacity);
                var fresh = MatchFileDialogAdapter(hwnd, sbClass.ToString(), GetProcessName(hwnd),
                    TimeSpan.FromMilliseconds(ExplorerActivePathPoller.RetryReadBudgetMs));

                // Only a different answer is a repair. Never demote to null here: a dialog that has begun
                // torn down answers "not a file dialog" to a re-read, and adopting that would take the card
                // away from under a user who is still looking at it, while RefreshActiveWindowAdapters is
                // the right place to make that call.
                if (fresh is null || ReferenceEquals(fresh, held)) continue;

                lock (StateLock)
                {
                    // The window this was about is gone by now, so this answer belongs to nothing.
                    if (_activeHwnd != hwnd) return;
                    ActiveAdapter = fresh;
                    _isActiveWindowDialog = true;
                }

                Logger.Log(
                    $"[ExplorerTracker] Dialog 0x{hwnd:x} re-derived on re-ask {ask}: "
                    + $"{held?.GetType().Name ?? "none"} -> {fresh.GetType().Name}.",
                    LogLevel.Debug);
                // The dialog can be measured now, which is the edge the card hangs from: same signal the
                // mirrored move uses, because what changed is exactly that -- an input to the placement.
                MoveActiveWindow();
                return;
            }

            Logger.Log(
                $"[ExplorerTracker] Dialog 0x{hwnd:x} still resolves to the same adapter after "
                + $"{ExplorerActivePathPoller.UnclaimedDialogRetryLimit} re-asks.",
                LogLevel.Debug);
        });
    }

    public void SetActiveInlineAdapterDirectly(IInlineSearchAdapter? adapter, IntPtr hwnd)
    {
        lock (StateLock)
        {
            ActiveInlineAdapter = adapter;
            _activeHwnd = hwnd;
            IsExplorerOrDesktopActive = adapter != null;
            if (adapter != null && hwnd != IntPtr.Zero)
            {
                var windowTitle = new StringBuilder(256);
                ExplorerNativeHooks.GetWindowText(hwnd, windowTitle, windowTitle.Capacity);
                var sbClass = new StringBuilder(256);
                ExplorerNativeHooks.GetClassName(hwnd, sbClass, sbClass.Capacity);
                RaiseExplorerActivated(hwnd, windowTitle.ToString(), sbClass.ToString(), false);
            }
        }
    }
    // Re-broadcasts whatever this tracker already believes is currently active, without re-deriving
    // anything -- used to bring a freshly (re)connected IPC client up to date. Needed because Start()'s
    // very first activation check runs synchronously at Hook-process startup, which routinely completes
    // before the App has finished connecting over the pipe; that one-time startup snapshot was the only
    // chance the App had to learn the true initial state, and HookIpcServer discards anything queued
    // before a connection completes (see its own comment), so silently missing it left the App's mirror
    // stuck at its all-zero/all-false defaults (e.g. IsDesktop stuck false) until the next real
    // foreground change corrected it -- see InlineSearchWindowPositioner, whose IsDesktop branch never
    // ran in that window, leaving the inline search window wherever it last happened to be.
    public void PublishCurrentState()
    {
        if (_activeHwnd == IntPtr.Zero) return;
        var windowTitle = new StringBuilder(256);
        ExplorerNativeHooks.GetWindowText(_activeHwnd, windowTitle, windowTitle.Capacity);
        var sbClass = new StringBuilder(256);
        ExplorerNativeHooks.GetClassName(_activeHwnd, sbClass, sbClass.Capacity);
        RaiseExplorerActivated(_activeHwnd, windowTitle.ToString(), sbClass.ToString(), IsDesktop);
        if (!string.IsNullOrEmpty(LastPath))
            RaisePathCaptured(LastPath, IsDesktop, IsActiveWindowDialog);
    }
    public string? ActivePath => LastPath;
    public uint AppProcessId { get; set; }
    public event Action<IntPtr, string, string, bool>? OnExplorerActivated;
    public event Action? OnExplorerDeactivated;
    public event Action<string, bool, bool>? OnPathCaptured;
    public event Action? OnActiveWindowMoved;
    public event Action<string>? OnError;
    // ExplorerActivePathPoller calls this for the foreground window on every system-wide WinEvent it
    // receives -- any window anywhere moving, resizing or renaming -- so it goes through
    // ProcessNameResolver rather than Process.GetProcessById, which would enumerate every process on the
    // machine and leave behind a finalizable object each time.
    internal string GetProcessName(IntPtr hwnd)
    {
        ExplorerNativeHooks.GetWindowThreadProcessId(hwnd, out var pid);
        return ProcessNameResolver.GetNameWithoutExtension(pid);
    }
    public void UpdateActiveWindow(IntPtr hwnd, string title, string className, bool isDesktop)
    {
        // Multi-field tracker mutations go through StateLock: the WinEvent tracker thread, the
        // keyboard hook thread and (in the App process) the IPC mirror all touch these fields, and
        // a torn combination -- new window's hwnd with the old window's dialog flag -- briefly
        // pointed Quick Switch and inline search at the wrong window.
        lock (StateLock)
        {
            ActiveHwnd = hwnd;
            IsExplorerOrDesktopActive = true;
            IsDesktop = isDesktop;
            IsActiveWindowExplorer = ActiveInlineAdapter?.IsFileExplorer ?? false;
            if (!IsActiveWindowDialog)
            {
                LastActiveExplorerClassName = className;
                LastActiveExplorerWindowTitle = title;
            }
            RaiseExplorerActivated(hwnd, title, className, isDesktop);
        }
    }
    public void DeactivateWindow() => Deactivate();
    // Re-derives full state (IsActiveWindowDialog, ActiveAdapter, dialog/path tracking, ...) for
    // whatever window is ACTUALLY foreground right now, instead of just wiping everything to "nothing
    // is active" -- see KeyboardHookService's own synchronous self-correction check, which used to call
    // DeactivateWindow() here and could clear IsActiveWindowDialog=true a few lines before Quick Switch
    // read it on the very same keystroke, if the async WinEvent hadn't caught up yet (e.g. right after a
    // "foreground became nothing" transition Explorer can produce, which carries hwnd==0 and is dropped
    // by WinEventProc, leaving ActiveHwnd stale until the next real foreground window shows up).
    public void ReclassifyActiveWindow(IntPtr hwnd) => _classifier.CheckActiveWindow(hwnd);

    // Bounded self-correction for LL hook threads: a reclassification that waits on the tracker lock
    // (e.g. the WinEvent tracker thread is stuck inside a slow plugin read) or that runs slow plugin
    // reads itself can stall the keyboard hook callback past LowLevelHooksTimeout, which gets the hook
    // silently dropped by Windows. Skip on a contended lock -- the WinEvent-based tracker remains
    // authoritative and will catch up -- and keep plugin reads to a fraction of a typical hook timeout.
    public void ReclassifyActiveWindowBounded(IntPtr hwnd)
        => _classifier.CheckActiveWindow(hwnd, lockWaitMs: 50, pluginTimeoutMs: 300);
    public void UpdatePath(string path, bool isDesktop, bool? isDialog = null)
    {
        if (PathNormalizer != null)
            path = PathNormalizer(path) ?? string.Empty;
        lock (StateLock)
        {
            LastPath = path;
            Logger.Log($"[ExplorerTracker] UpdatePath captured path: {path} (isDesktop={isDesktop})", LogLevel.Debug);
            var pathIsDialog = isDialog ?? IsActiveWindowDialog;
            if (isDialog == true && !_isActiveWindowDialog && _activeHwnd != IntPtr.Zero)
            {
                // The process that owns the WinEvent path has claimed this window as a dialog, while this
                // process's own probe matched nothing -- a read that timed out, or a dialog still building.
                // The claim wins: the card is what the user is waiting on, and everything the adapter would
                // have answered for -- the anchor rect, the folder it feeds -- already has a fallback.
                //
                // Only while a window is actually tracked, though: a path event that arrives after a
                // deactivation carries the claim for a window this tracker has let go of, and honouring it
                // there would say "a dialog is active" with no dialog to point at.
                Logger.Log(
                    $"[ExplorerTracker] Path event reports 0x{_activeHwnd:x} as a claimed dialog; "
                    + "no adapter matched it in this process.",
                    LogLevel.Debug);
                _isActiveWindowDialog = true;
                RederiveActiveDialogAdapterIfStale();
            }

            if (!pathIsDialog) _dialogTracker.SetLastActiveExplorerPath(path);
            RaisePathCaptured(path, isDesktop, pathIsDialog);
        }
    }
    public void MoveActiveWindow() => OnActiveWindowMoved?.Invoke();
    public void RaiseErrorExternal(string msg) => RaiseError(msg);
    internal void RaiseExplorerActivated(IntPtr hwnd, string title, string cls, bool isDesktop) => OnExplorerActivated?.Invoke(hwnd, title, cls, isDesktop);
    internal void RaisePathCaptured(string path, bool isDesktop, bool isDialog) => OnPathCaptured?.Invoke(path, isDesktop, isDialog);
    internal void RaiseError(string msg) => OnError?.Invoke(msg);
    public ExplorerTracker()
    {
        _classifier = new ExplorerWindowClassifier(this, _dialogTracker);
        _pathPoller = new ExplorerActivePathPoller(_classifier);
    }
    public void Start()
    {
        if (_isRunning) return;
        _winEventDelegate = new ExplorerNativeHooks.WinEventDelegate(WinEventProc);
        _hForegroundHook = ExplorerNativeHooks.SetWinEventHook(
            ExplorerNativeHooks.EVENT_SYSTEM_FOREGROUND, ExplorerNativeHooks.EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero, _winEventDelegate, 0, 0, ExplorerNativeHooks.WINEVENT_OUTOFCONTEXT);
        _hNameChangeHook = ExplorerNativeHooks.SetWinEventHook(
            ExplorerNativeHooks.EVENT_OBJECT_NAMECHANGE, ExplorerNativeHooks.EVENT_OBJECT_NAMECHANGE,
            IntPtr.Zero, _winEventDelegate, 0, 0, ExplorerNativeHooks.WINEVENT_OUTOFCONTEXT);
        _hLocationChangeHook = ExplorerNativeHooks.SetWinEventHook(
            ExplorerNativeHooks.EVENT_OBJECT_LOCATIONCHANGE, ExplorerNativeHooks.EVENT_OBJECT_LOCATIONCHANGE,
            IntPtr.Zero, _winEventDelegate, 0, 0, ExplorerNativeHooks.WINEVENT_OUTOFCONTEXT);
        _hFocusHook = ExplorerNativeHooks.SetWinEventHook(
            ExplorerNativeHooks.EVENT_OBJECT_FOCUS, ExplorerNativeHooks.EVENT_OBJECT_FOCUS,
            IntPtr.Zero, _winEventDelegate, 0, 0, ExplorerNativeHooks.WINEVENT_OUTOFCONTEXT);
        if (_hForegroundHook == IntPtr.Zero || _hNameChangeHook == IntPtr.Zero || _hLocationChangeHook == IntPtr.Zero || _hFocusHook == IntPtr.Zero)
        {
            Stop();
            Logger.Log("[ExplorerTracker] Failed to register WinEvent hooks!", LogLevel.Error);
            return;
        }
        _isRunning = true;
        Logger.Log("[ExplorerTracker] Started.");
        _classifier.CheckActiveWindow(ExplorerNativeHooks.GetForegroundWindow());
    }
    public void Stop()
    {
        if (_hForegroundHook != IntPtr.Zero) { ExplorerNativeHooks.UnhookWinEvent(_hForegroundHook); _hForegroundHook = IntPtr.Zero; }
        if (_hNameChangeHook != IntPtr.Zero) { ExplorerNativeHooks.UnhookWinEvent(_hNameChangeHook); _hNameChangeHook = IntPtr.Zero; }
        if (_hLocationChangeHook != IntPtr.Zero) { ExplorerNativeHooks.UnhookWinEvent(_hLocationChangeHook); _hLocationChangeHook = IntPtr.Zero; }
        if (_hFocusHook != IntPtr.Zero) { ExplorerNativeHooks.UnhookWinEvent(_hFocusHook); _hFocusHook = IntPtr.Zero; }
        _winEventDelegate = null;
        _isRunning = false;
        LastPath = null;
        LastActiveHwnd = IntPtr.Zero;
        IsExplorerOrDesktopActive = false;
        IsDesktop = false;
        ActiveHwnd = IntPtr.Zero;
        _dialogTracker.Clear();
        Logger.Log("[ExplorerTracker] Stopped.");
    }
    public bool TryGetActiveWindowRect(out RECT rect)
    {
        rect = default;
        if (ActiveHwnd == IntPtr.Zero) return false;
        if (ActiveAdapter != null && ActiveAdapter.GetDockBounds(ActiveHwnd, out var r1))
        {
            rect = new RECT { Left = r1.Left, Top = r1.Top, Right = r1.Right, Bottom = r1.Bottom };
            return true;
        }
        if (ActiveInlineAdapter != null && ActiveInlineAdapter.GetDockBounds(ActiveHwnd, out var r2))
        {
            rect = new RECT { Left = r2.Left, Top = r2.Top, Right = r2.Right, Bottom = r2.Bottom };
            return true;
        }
        var nativeRect = new ExplorerNativeHooks.RECT();
        if (ExplorerNativeHooks.DwmGetWindowAttribute(ActiveHwnd, ExplorerNativeHooks.DWMWA_EXTENDED_FRAME_BOUNDS, out nativeRect, System.Runtime.InteropServices.Marshal.SizeOf<ExplorerNativeHooks.RECT>()) == 0 ||
            ExplorerNativeHooks.GetWindowRect(ActiveHwnd, out nativeRect))
        {
            rect = new RECT { Left = nativeRect.Left, Top = nativeRect.Top, Right = nativeRect.Right, Bottom = nativeRect.Bottom };
            return true;
        }
        return false;
    }

    /// <summary>
    /// Where the active dialog's own target field is, when that dialog's adapter can see it. False for a
    /// non-dialog window, and for a dialog whose adapter does not opt in -- see
    /// <see cref="IFileDialogAdapter.TryGetTargetFieldBounds"/>.
    /// </summary>
    public bool TryGetTargetFieldRect(out RECT rect)
    {
        rect = default;
        if (ActiveHwnd == IntPtr.Zero || ActiveAdapter == null)
            return false;
        if (!ActiveAdapter.TryGetTargetFieldBounds(ActiveHwnd, out var bounds))
            return false;
        if (!IsUsableBounds(bounds))
            return false;

        rect = ToRect(bounds);
        return true;
    }

    /// <summary>
    /// The active dialog's own file list, when that dialog's adapter can see it. False for a non-dialog
    /// window, and for a dialog whose adapter does not opt in -- see
    /// <see cref="IFileDialogAdapter.TryGetFileListBounds"/>.
    /// </summary>
    public bool TryGetFileListRect(out RECT rect)
    {
        rect = default;
        if (ActiveHwnd == IntPtr.Zero || ActiveAdapter == null)
            return false;
        if (!ActiveAdapter.TryGetFileListBounds(ActiveHwnd, out var bounds))
            return false;
        if (!IsUsableBounds(bounds))
            return false;

        rect = ToRect(bounds);
        return true;
    }

    // Reported across a process boundary by someone else's UI framework, which is exactly where an empty or
    // bogus rect comes from; a region with no area cannot anchor anything.
    private static bool IsUsableBounds(AdapterRect bounds) =>
        bounds.Right > bounds.Left && bounds.Bottom > bounds.Top;

    private static RECT ToRect(AdapterRect b) =>
        new() { Left = b.Left, Top = b.Top, Right = b.Right, Bottom = b.Bottom };

    private void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        if (!_isRunning || hwnd == IntPtr.Zero) return;
        if (idObject != 0) return;
        if (eventType == ExplorerNativeHooks.EVENT_SYSTEM_FOREGROUND)
        {
            _classifier.CheckActiveWindow(hwnd);
        }
        else if (eventType == ExplorerNativeHooks.EVENT_OBJECT_NAMECHANGE)
        {
            if (hwnd == ExplorerNativeHooks.GetForegroundWindow())
                _classifier.CheckActiveWindow(hwnd);
        }
        else if (eventType == ExplorerNativeHooks.EVENT_OBJECT_LOCATIONCHANGE)
        {
            if (hwnd == ActiveHwnd && IsActiveWindowDialog)
                OnActiveWindowMoved?.Invoke();
        }
        else if (eventType == ExplorerNativeHooks.EVENT_OBJECT_FOCUS)
        {
            var root = ExplorerNativeHooks.GetAncestor(hwnd, ExplorerNativeHooks.GA_ROOTOWNER);
            if (root == ExplorerNativeHooks.GetForegroundWindow())
                _classifier.CheckActiveWindow(root);
        }
        _pathPoller.Poll(this, eventType);
    }
    internal void Deactivate()
    {
        // Reentrant-safe: the classifier calls this while already holding StateLock.
        lock (StateLock)
        {
            var wasActive = IsExplorerOrDesktopActive;
            IsExplorerOrDesktopActive = IsDesktop = IsActiveWindowDialog = IsActiveWindowExplorer = false;
            ActiveHwnd = LastActiveHwnd = IntPtr.Zero;
            LastPath = null;
            if (wasActive) OnExplorerDeactivated?.Invoke();
        }
    }
    public void Dispose()
    {
        Stop();
        // Only on Dispose, not in Stop: Stop/Start is a restart, and the poller's deferred-poll timer is
        // owned for the tracker's whole life.
        _pathPoller.Dispose();
    }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
    }
    public static IntPtr FindSubEditBox(IntPtr parent) => ExplorerNativeHooks.FindSubEditBox(parent);
}
