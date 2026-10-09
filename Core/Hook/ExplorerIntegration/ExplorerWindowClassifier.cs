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

    // Default plugin-read budget for tracker-owned threads (WinEvent tracker, poller): long enough for
    // a healthy Explorer COM path read on a cold cache, short enough that a hung plugin cannot hold the
    // tracker lock forever. The LL hook path passes a much tighter budget -- see ExplorerTracker.
    internal const int DefaultPluginTimeoutMs = 2000;

    public void CheckActiveWindow(IntPtr hwnd)
        => CheckActiveWindow(hwnd, Timeout.Infinite, DefaultPluginTimeoutMs);

    // lockWaitMs bounds how long the caller waits for the tracker lock; pluginTimeoutMs bounds each
    // plugin adapter/collector read inside. The LL keyboard hook thread uses both bounds: a stall past
    // LowLevelHooksTimeout gets the hook silently dropped by Windows, so it skips a contended lock
    // (the WinEvent tracker remains authoritative) and never waits long on plugin I/O.
    public void CheckActiveWindow(IntPtr hwnd, int lockWaitMs, int pluginTimeoutMs)
    {
        if (hwnd == IntPtr.Zero) return;

        if (!Monitor.TryEnter(_lock, lockWaitMs))
        {
            Logger.Log("[ExplorerTracker] Reclassification skipped: tracker lock busy.", LogLevel.Debug);
            return;
        }

        try
        {
            if (_tracker.IsActiveWindowDialog && _tracker.ActiveHwnd != IntPtr.Zero && !ExplorerNativeHooks.IsWindow(_tracker.ActiveHwnd))
            {
                _tracker.Deactivate();
            }

            if (ExplorerFocusChangeFilter.IsIgnored(_tracker, hwnd))
                return;

            var dialogHwnd = FindMatchingDialogWindow(hwnd, pluginTimeoutMs, out var adapter);
            if (dialogHwnd != IntPtr.Zero && adapter != null)
            {
                var previousWasPathProvider = _tracker.IsExplorerOrDesktopActive && !_tracker.IsActiveWindowDialog;
                TrackFileDialogWindow(dialogHwnd, previousWasPathProvider, TimeSpan.FromMilliseconds(pluginTimeoutMs));
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
                        // Bounded dispatch: plugin code can hang in cross-process COM calls, and
                        // this runs under the tracker lock (the LL hook thread contends on it).
                        // A timeout keeps the last known path instead of wiping it -- a hung read
                        // says nothing about the path actually being gone.
                        var pluginReadTimeout = TimeSpan.FromMilliseconds(pluginTimeoutMs);
                        var activePath = ExplorerStaInvoker.RunOnStaWithTimeout(
                            () => collector.TryGetPath(focusedHwnd, activeClassName, rootHwnd, windowClassName, processName),
                            (string?)null, pluginReadTimeout, out var collectorTimedOut);
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

                        if (!collectorTimedOut)
                        {
                            if (!string.IsNullOrEmpty(activePath))
                            {
                                if (_dialogTracker.LastActiveExplorerPath != activePath)
                                    _dialogTracker.SetLastActiveExplorerPath(activePath);

                                if (activePath != _tracker.LastPath)
                                {
        // Remember the provider while it is the active window: that is when its scope is still readable.
        _tracker.RememberPathProvider(_tracker.ActiveInlineAdapter, _tracker.ActiveHwnd);
                                    _tracker.UpdatePath(activePath, isDesktop);
                                }
                            }
                            else if (!string.IsNullOrEmpty(_tracker.LastPath))
                            {
                                _tracker.UpdatePath(string.Empty, isDesktop);
                            }
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
        finally
        {
            Monitor.Exit(_lock);
        }
    }

    private void TrackFileDialogWindow(IntPtr mainDialog, bool previousWasPathProvider, TimeSpan pluginReadTimeout)
    {
        // Capture the actual departing provider before ActiveHwnd selects the dialog's adapters.
        var providerAdapter = previousWasPathProvider ? _tracker.ActiveInlineAdapter : null;
        var providerHwnd = _tracker.ActiveHwnd;
        _tracker.IsExplorerOrDesktopActive = true;
        _tracker.IsDesktop = false;
        _tracker.ActiveHwnd = mainDialog;


        // The folder the user is browsing lives in the path provider; the dialog's own path does not.
        // Read it once here, for both uses: the dialog tracker's follow decision, and the publication
        // below -- without that, the App's LastActiveExplorerPath (what the inline list offers as
        // "jump to the open folder") never learns the file manager's folder at all.
        // Prefer the window that was active just before the dialog; fall back to the provider remembered
        // while it was active -- the usual case with more than one file manager open, or when focus passed
        // through an unrelated window on its way to the dialog.
        if (previousWasPathProvider) _tracker.RememberPathProvider(providerAdapter, providerHwnd);
        var scopeAdapter = providerAdapter ?? (previousWasPathProvider ? null : _tracker.LastPathProviderAdapter);
        var scopeHwnd = previousWasPathProvider ? providerHwnd : _tracker.LastPathProviderHwnd;
        var providerScope = scopeAdapter == null || scopeHwnd == IntPtr.Zero ? null : ExplorerStaInvoker.RunOnStaWithTimeout(
            () => scopeAdapter.GetSearchScope(scopeHwnd), null, pluginReadTimeout);

        _ = _dialogTracker.HandleDialogSeenAsync(mainDialog, _tracker.ActiveAdapter, previousWasPathProvider,
            providerScope == null ? null : () => providerScope);

        // Bounded dispatch (see the collector loop above). On timeout the null fallback flows into the
        // keep-last-known branch below, matching the empty-result handling.
        var activePath = ExplorerStaInvoker.RunOnStaWithTimeout(
            () => _tracker.ActiveAdapter?.GetCurrentPath(mainDialog), null, pluginReadTimeout);
        if (string.IsNullOrEmpty(activePath))
        {
            // The adapter couldn't determine a path (e.g. FolderBrowserDialogAdapter always returns
            // null -- SHBrowseForFolder has no safe way to query the current selection externally).
            // Keep showing whatever was last known instead of resetting the search scope to nothing.
            activePath = _tracker.LastPath ?? string.Empty;
        }

        var windowTitle = new StringBuilder(256);
        ExplorerNativeHooks.GetWindowText(mainDialog, windowTitle, windowTitle.Capacity);

        var sbCls2 = new StringBuilder(256);
        ExplorerNativeHooks.GetClassName(mainDialog, sbCls2, sbCls2.Capacity);

        if (mainDialog != _tracker.LastActiveHwnd)
        {
            _tracker.LastActiveHwnd = mainDialog;
            _tracker.RaiseExplorerActivated(mainDialog, windowTitle.ToString(), sbCls2.ToString(), false);
        }

        // Publish the file manager's folder, not the dialog's: this is the value the App keeps as
        // LastActiveExplorerPath and hands to the inline list's "jump to the open folder" row. The
        // dialog's own path still goes out below, flagged as a dialog path, for last-directory tracking.
        _tracker.RaisePathCaptured(string.IsNullOrEmpty(providerScope) ? _tracker.LastPath : providerScope, false, false);
        _tracker.RaisePathCaptured(_tracker.LastPath, false, true);
    }

    private IntPtr FindMatchingDialogWindow(IntPtr hwnd, int pluginTimeoutMs, out IFileDialogAdapter? adapter)
    {
        var current = hwnd;
        while (current != IntPtr.Zero)
        {
            var sbClass = new StringBuilder(256);
            ExplorerNativeHooks.GetClassName(current, sbClass, sbClass.Capacity);
            var className = sbClass.ToString();

            ExplorerNativeHooks.GetWindowThreadProcessId(current, out var pid);
            var processName = ProcessNameResolver.GetNameWithoutExtension(pid);

            // Bounded, like every other plugin read here -- see ExplorerStaInvoker's own summary, which says
            // exactly that. An adapter's CanHandle is a cross-process read for the dialogs whose widgets carry
            // no window handles (WPS goes through UI Automation) and for the ones that walk child windows, and
            // the target's thread is busiest precisely in the moment this is asked of it: the instant a dialog
            // takes the foreground. Asked directly, it parks the WinEvent tracker thread for as long as the
            // other process likes -- which is not "this one window is misclassified" but "nothing in the
            // session is tracked any more", and the log shows it as total silence rather than as a wrong answer.
            var matched = ExplorerStaInvoker.RunOnStaWithTimeout(
                () => FileDialogAdapterRegistry.GetMatchingAdapter(current, className, processName),
                (IFileDialogAdapter?)null,
                TimeSpan.FromMilliseconds(pluginTimeoutMs));
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
