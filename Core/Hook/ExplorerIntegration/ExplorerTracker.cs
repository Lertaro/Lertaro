using System.Text;
using Lertaro.PluginSdk.Registries;
using Lertaro.PluginSdk.Abstractions.Plugins.WindowAdapters;
namespace Lertaro.Core.Hook;

public class ExplorerTracker : IDisposable
{
    internal object StateLock { get; } = new();

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
        ActiveAdapter = FileDialogAdapterRegistry.GetMatchingAdapter(_activeHwnd, className, processName);
        _isActiveWindowDialog = ActiveAdapter != null;
        ActiveInlineAdapter = InlineSearchAdapterRegistry.GetMatchingAdapter(_activeHwnd, className, processName);
        IsActiveWindowExplorer = !IsDesktop && (ActiveInlineAdapter?.IsFileExplorer ?? false);
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
            RaisePathCaptured(LastPath, IsDesktop);
    }
    public string? ActivePath => LastPath;
    public uint AppProcessId { get; set; }
    public event Action<IntPtr, string, string, bool>? OnExplorerActivated;
    public event Action? OnExplorerDeactivated;
    public event Action<string, bool>? OnPathCaptured;
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
    public void DeactivateWindow() => Deactivate();
    // Re-derives full state (IsActiveWindowDialog, ActiveAdapter, dialog/path tracking, ...) for
    // whatever window is ACTUALLY foreground right now, instead of just wiping everything to "nothing
    // is active" -- see KeyboardHookService's own synchronous self-correction check, which used to call
    // DeactivateWindow() here and could clear IsActiveWindowDialog=true a few lines before Quick Switch
    // read it on the very same keystroke, if the async WinEvent hadn't caught up yet (e.g. right after a
    // "foreground became nothing" transition Explorer can produce, which carries hwnd==0 and is dropped
    // by WinEventProc, leaving ActiveHwnd stale until the next real foreground window shows up).
    public void ReclassifyActiveWindow(IntPtr hwnd) => _classifier.CheckActiveWindow(hwnd);
    public void UpdatePath(string path, bool isDesktop)
    {
        if (PathNormalizer != null)
            path = PathNormalizer(path) ?? string.Empty;
        LastPath = path;
        Logger.Log($"[ExplorerTracker] UpdatePath captured path: {path} (isDesktop={isDesktop})", LogLevel.Debug);
        if (!IsActiveWindowDialog) _dialogTracker.SetLastActiveExplorerPath(path);
        RaisePathCaptured(path, isDesktop);
    }
    public void MoveActiveWindow() => OnActiveWindowMoved?.Invoke();
    public void RaiseErrorExternal(string msg) => RaiseError(msg);
    internal void RaiseExplorerActivated(IntPtr hwnd, string title, string cls, bool isDesktop) => OnExplorerActivated?.Invoke(hwnd, title, cls, isDesktop);
    internal void RaisePathCaptured(string path, bool isDesktop) => OnPathCaptured?.Invoke(path, isDesktop);
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
        var wasActive = IsExplorerOrDesktopActive;
        IsExplorerOrDesktopActive = IsDesktop = IsActiveWindowDialog = IsActiveWindowExplorer = false;
        ActiveHwnd = LastActiveHwnd = IntPtr.Zero;
        LastPath = null;
        if (wasActive) OnExplorerDeactivated?.Invoke();
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
