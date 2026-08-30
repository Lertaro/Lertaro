using System.Text;
using Lertaro.PluginSdk.Registries;

using Lertaro.PluginSdk.Abstractions.Plugins.WindowAdapters;
using Lertaro.Core.Hook.InlineSearch;
namespace Lertaro.Core.Hook;

/// <summary>
/// Handles window classification and path tracking for ExplorerTracker,
/// delegating path collection to registered IActivePathCollector plugins.
/// </summary>
internal sealed class ExplorerWindowClassifier
{
    private readonly ExplorerTracker _tracker;
    private readonly FileDialogNavigationTracker _dialogTracker;

    // ExplorerTracker's own WinEvent hooks run on a dedicated thread (HookProcess's _trackerThread),
    // separate from the WH_KEYBOARD_LL keyboard hook's thread -- and KeyboardHookService now also calls
    // into CheckActiveWindow (via ExplorerTracker.ReclassifyActiveWindow) as a self-correction when its
    // synchronous, per-keystroke foreground check disagrees with the tracker's last-known state. Without
    // this, those two threads could both be mutating the tracker's unsynchronized fields
    // (ActiveHwnd/IsActiveWindowDialog/ActiveAdapter/LastPath/...) concurrently.
    private readonly object _lock;

    public ExplorerWindowClassifier(ExplorerTracker tracker, FileDialogNavigationTracker dialogTracker)
    {
        _tracker = tracker;
        _dialogTracker = dialogTracker;
        _lock = tracker.StateLock;
    }

    public void CheckActiveWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;

        lock (_lock)
        {
            try
            {
                if (_tracker.IsActiveWindowDialog && _tracker.ActiveHwnd != IntPtr.Zero && !ExplorerNativeHooks.IsWindow(_tracker.ActiveHwnd))
                {
                    _tracker.Deactivate();
                }

                if (IsFocusChangeIgnored(hwnd))
                    return;

                var dialogHwnd = FindMatchingDialogWindow(hwnd, out var adapter);
                if (dialogHwnd != IntPtr.Zero && adapter != null)
                {
                    var previousWasPathProvider = _tracker.IsExplorerOrDesktopActive && !_tracker.IsActiveWindowDialog;
                    TrackFileDialogWindow(dialogHwnd, previousWasPathProvider);
                    return;
                }

                var rootHwnd = ExplorerNativeHooks.GetAncestor(hwnd, ExplorerNativeHooks.GA_ROOTOWNER);
                if (rootHwnd == IntPtr.Zero) rootHwnd = hwnd;

                var isDesktop = ExplorerNativeHooks.IsDesktopWindow(rootHwnd, out var windowClassName);
                Logger.Log($"[ExplorerTracker] Active window: HWND=0x{hwnd:X}, Root=0x{rootHwnd:X}, Class={windowClassName}, isDesktop={isDesktop}", LogLevel.Debug);

                // Resolve the actual focused control handle inside the active window's thread
                var focusedHwnd = IntPtr.Zero;
                var activeClassName = string.Empty;
                try
                {
                    var threadId = KeyboardNativeMethods.GetWindowThreadProcessId(rootHwnd, out _);
                    var guiInfo = new KeyboardNativeMethods.GUITHREADINFO();
                    guiInfo.cbSize = System.Runtime.InteropServices.Marshal.SizeOf(guiInfo);
                    if (KeyboardNativeMethods.GetGUIThreadInfo(threadId, ref guiInfo) && guiInfo.hwndFocus != IntPtr.Zero)
                    {
                        focusedHwnd = guiInfo.hwndFocus;
                        var sbActiveCls = new StringBuilder(256);
                        KeyboardNativeMethods.GetClassName(focusedHwnd, sbActiveCls, sbActiveCls.Capacity);
                        activeClassName = sbActiveCls.ToString();
                    }
                }
                catch { }

                if (focusedHwnd == IntPtr.Zero)
                {
                    focusedHwnd = hwnd;
                    var sbActiveCls = new StringBuilder(256);
                    ExplorerNativeHooks.GetClassName(hwnd, sbActiveCls, sbActiveCls.Capacity);
                    activeClassName = sbActiveCls.ToString();
                }

                var processName = _tracker.GetProcessName(rootHwnd);

                // Delegate active path collection to registered plugins
                var collectors = ActivePathCollectorRegistry.GetCollectors();
                var handledByPlugin = false;

                foreach (var collector in collectors)
                {
                    try
                    {
                        if (collector.CanHandle(rootHwnd, windowClassName, processName))
                        {
                            var activePath = collector.TryGetPath(focusedHwnd, activeClassName, rootHwnd, windowClassName, processName);
                            handledByPlugin = true;
                            _tracker.ActiveHwnd = rootHwnd;
                            _tracker.IsExplorerOrDesktopActive = true;
                            _tracker.IsDesktop = isDesktop;
                            _tracker.IsActiveWindowDialog = false;
                            _tracker.IsActiveWindowExplorer = !isDesktop && (_tracker.ActiveInlineAdapter?.IsFileExplorer ?? false);
                            _tracker.LastActiveExplorerClassName = windowClassName;

                            if (rootHwnd != _tracker.LastActiveHwnd)
                            {
                                _tracker.LastActiveHwnd = rootHwnd;
                                var windowTitle = new StringBuilder(256);
                                ExplorerNativeHooks.GetWindowText(rootHwnd, windowTitle, windowTitle.Capacity);
                                _tracker.RaiseExplorerActivated(rootHwnd, windowTitle.ToString(), windowClassName, isDesktop);
                            }

                            if (!string.IsNullOrEmpty(activePath))
                            {
                                if (_dialogTracker.LastActiveExplorerPath != activePath)
                                    _dialogTracker.SetLastActiveExplorerPath(activePath);

                                if (activePath != _tracker.LastPath)
                                {
                                    _tracker.UpdatePath(activePath, isDesktop);
                                }
                            }
                            else if (!string.IsNullOrEmpty(_tracker.LastPath))
                            {
                                _tracker.UpdatePath(string.Empty, isDesktop);
                            }
                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"[ExplorerTracker] Error invoking active path collector '{collector.Name}': {ex.Message}", LogLevel.Error);
                    }
                }

                if (handledByPlugin)
                {
                    return;
                }

                var matchedAdapter = FileDialogAdapterRegistry.GetMatchingAdapter(rootHwnd, windowClassName, processName);
                if (matchedAdapter != null)
                {
                    _tracker.IsExplorerOrDesktopActive = true;
                    _tracker.IsDesktop = false;
                    _tracker.ActiveHwnd = rootHwnd;
                    _tracker.IsActiveWindowExplorer = false;
                }
                else
                {
                    var matchedInlineAdapter = InlineSearchAdapterRegistry.GetMatchingAdapter(rootHwnd, windowClassName, processName);
                    if (matchedInlineAdapter != null)
                    {
                        _tracker.IsExplorerOrDesktopActive = false;
                        _tracker.IsDesktop = false;
                        _tracker.IsActiveWindowExplorer = false;
                        _tracker.ActiveHwnd = rootHwnd;

                        if (rootHwnd != _tracker.LastActiveHwnd)
                        {
                            _tracker.LastActiveHwnd = rootHwnd;
                            var windowTitle = new StringBuilder(256);
                            ExplorerNativeHooks.GetWindowText(rootHwnd, windowTitle, windowTitle.Capacity);
                            _tracker.RaiseExplorerActivated(rootHwnd, windowTitle.ToString(), windowClassName, false);
                        }
                    }
                    else
                    {
                        if (_tracker.IsActiveWindowDialog && _tracker.ActiveHwnd != IntPtr.Zero)
                        {
                            var fgHwnd = ExplorerNativeHooks.GetForegroundWindow();
                            if (ExplorerWindowRelationshipHelper.IsDescendantOrOwned(_tracker.ActiveHwnd, fgHwnd) || IsImeWindow(fgHwnd))
                            {
                                return;
                            }
                        }
                        _tracker.Deactivate();
                    }
                }
            }
            catch (Exception ex)
            {
                _tracker.RaiseError(ex.Message);
            }
        }
    }

    private bool IsFocusChangeIgnored(IntPtr hwnd)
    {
        var sbClass = new StringBuilder(256);
        ExplorerNativeHooks.GetClassName(hwnd, sbClass, sbClass.Capacity);
        var className = sbClass.ToString();
        if (className.Contains("InputSwitch", StringComparison.OrdinalIgnoreCase)) return true;
        ExplorerNativeHooks.GetWindowThreadProcessId(hwnd, out var activePid);
        if (activePid == Environment.ProcessId || (activePid != 0 && activePid == _tracker.AppProcessId))
        {
            if (className.Equals("#32770", StringComparison.OrdinalIgnoreCase)) return false;
            var rootHwnd = ExplorerNativeHooks.GetAncestor(hwnd, ExplorerNativeHooks.GA_ROOTOWNER);
            if (rootHwnd == IntPtr.Zero) rootHwnd = hwnd;
            var processName = _tracker.GetProcessName(rootHwnd);
            if (ActivePathCollectorRegistry.GetCollectors()
                .Any(collector => collector.CanHandle(rootHwnd, className, processName))) return false;
            return true;
        }
        if (_tracker.ActiveHwnd != IntPtr.Zero)
        {
            var rootHwnd = ExplorerNativeHooks.GetAncestor(hwnd, ExplorerNativeHooks.GA_ROOTOWNER);
            if (rootHwnd == IntPtr.Zero) rootHwnd = hwnd;
            if (rootHwnd == _tracker.ActiveHwnd)
            {
                return true;
            }
        }
        return false;
    }

    private void TrackFileDialogWindow(IntPtr mainDialog, bool previousWasPathProvider)
    {
        _tracker.IsExplorerOrDesktopActive = true;
        _tracker.IsDesktop = false;
        _tracker.ActiveHwnd = mainDialog;

        _dialogTracker.HandleDialogSeen(mainDialog, _tracker.ActiveAdapter, previousWasPathProvider);

        var activePath = _tracker.ActiveAdapter?.GetCurrentPath(mainDialog);
        if (string.IsNullOrEmpty(activePath))
        {
            // The adapter couldn't determine a path (e.g. FolderBrowserDialogAdapter always returns
            // null -- SHBrowseForFolder has no safe way to query the current selection externally).
            // Keep showing whatever was last known instead of resetting the search scope to nothing.
            activePath = _tracker.LastPath ?? string.Empty;
        }
        _tracker.LastPath = activePath;

        var windowTitle = new StringBuilder(256);
        ExplorerNativeHooks.GetWindowText(mainDialog, windowTitle, windowTitle.Capacity);

        var sbCls2 = new StringBuilder(256);
        ExplorerNativeHooks.GetClassName(mainDialog, sbCls2, sbCls2.Capacity);

        if (mainDialog != _tracker.LastActiveHwnd)
        {
            _tracker.LastActiveHwnd = mainDialog;
            _tracker.RaiseExplorerActivated(mainDialog, windowTitle.ToString(), sbCls2.ToString(), false);
        }

        _tracker.RaisePathCaptured(_tracker.LastPath, false);
    }

    private IntPtr FindMatchingDialogWindow(IntPtr hwnd, out IFileDialogAdapter? adapter)
    {
        var current = hwnd;
        while (current != IntPtr.Zero)
        {
            var sbClass = new StringBuilder(256);
            ExplorerNativeHooks.GetClassName(current, sbClass, sbClass.Capacity);
            var className = sbClass.ToString();

            ExplorerNativeHooks.GetWindowThreadProcessId(current, out var pid);
            var processName = ProcessNameResolver.GetNameWithoutExtension(pid);

            var matched = FileDialogAdapterRegistry.GetMatchingAdapter(current, className, processName);
            if (matched != null)
            {
                adapter = matched;
                return current;
            }

            current = ExplorerNativeHooks.GetParent(current);
        }

        adapter = null;
        return IntPtr.Zero;
    }

    private bool IsImeWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return false;
        var sbClass = new StringBuilder(256);
        ExplorerNativeHooks.GetClassName(hwnd, sbClass, sbClass.Capacity);
        var fgClass = sbClass.ToString();
        return fgClass.Contains("IME", StringComparison.OrdinalIgnoreCase) || fgClass.Contains("Candidate", StringComparison.OrdinalIgnoreCase) || fgClass.Contains("InputTip", StringComparison.OrdinalIgnoreCase) || fgClass.Contains("InputSwitch", StringComparison.OrdinalIgnoreCase);
    }
}
