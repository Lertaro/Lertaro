using System.Text;
using Lertaro.PluginSdk.Registries;
using Lertaro.Core.Hook.InlineSearch;
namespace Lertaro.Core.Hook;

internal sealed class ExplorerActivePathPoller : IDisposable
{
    // How long a moving window has to hold still before its position is taken as settled. Short enough to
    // be imperceptible on the occasions a move really did change the path, long enough that a drag or
    // resize -- which emits EVENT_OBJECT_LOCATIONCHANGE continuously, measured at roughly 200 a second --
    // produces one poll rather than hundreds.
    private const int LocationSettleMs = 200;

    private readonly ExplorerWindowClassifier _classifier;
    private readonly QuietPeriodScheduler _scheduler;
    private ExplorerTracker? _tracker;

    public ExplorerActivePathPoller(ExplorerWindowClassifier classifier)
    {
        _classifier = classifier;
        _scheduler = new QuietPeriodScheduler(() =>
        {
            var tracker = _tracker;
            if (tracker != null) PollCore(tracker);
        }, LocationSettleMs);
    }

    public void Poll(ExplorerTracker tracker, uint eventType)
    {
        _tracker = tracker;

        // A window moving or resizing says nothing about the tracked window's path most of the time, but it
        // does occasionally carry one (measured for Explorer, Total Commander and file dialogs alike), so it
        // cannot just be dropped. Wait for the movement to stop and poll once for the whole burst. Every
        // other event polls straight away, as all of them did before.
        if (eventType == ExplorerNativeHooks.EVENT_OBJECT_LOCATIONCHANGE)
        {
            _scheduler.RunWhenQuiet();
            return;
        }

        _scheduler.RunNow();
    }

    public void Dispose() => _scheduler.Dispose();

    private void PollCore(ExplorerTracker tracker)
    {
        var currentFg = ExplorerNativeHooks.GetForegroundWindow();
        if (currentFg != IntPtr.Zero && currentFg != tracker.ActiveHwnd)
        {
            var sbClass = new StringBuilder(256);
            ExplorerNativeHooks.GetClassName(currentFg, sbClass, sbClass.Capacity);
            var className = sbClass.ToString();
            var processName = tracker.GetProcessName(currentFg);
            if (FileDialogAdapterRegistry.GetMatchingAdapter(currentFg, className, processName) != null ||
                InlineSearchAdapterRegistry.GetMatchingAdapter(currentFg, className, processName) != null ||
                ActivePathCollectorRegistry.GetCollectors()
                    .Any(collector => collector.CanHandle(currentFg, className, processName)))
            {
                _classifier.CheckActiveWindow(currentFg);
            }
        }
        if (tracker.IsActiveWindowDialog && tracker.ActiveHwnd != IntPtr.Zero && tracker.ActiveAdapter != null)
        {
            var dialogHwnd = tracker.ActiveHwnd;
            var dialogAdapter = tracker.ActiveAdapter;
            var activePath = RunOnStaWithTimeout(() => dialogAdapter.GetCurrentPath(dialogHwnd), null, TimeSpan.FromSeconds(2));
            if (!IsObservedWindowStillActive(dialogHwnd, tracker.ActiveHwnd)) return;
            if (!string.IsNullOrEmpty(activePath) && activePath != tracker.LastPath)
            {
                tracker.UpdatePath(activePath, false);
            }
        }

        var polledByCollector = false;
        if (tracker.ActiveHwnd != IntPtr.Zero && tracker.ActiveInlineAdapter == null)
        {
            var collectorHwnd = tracker.ActiveHwnd;
            var sbClass = new StringBuilder(256);
            ExplorerNativeHooks.GetClassName(collectorHwnd, sbClass, sbClass.Capacity);
            var activeClass = sbClass.ToString();
            var collectors = ActivePathCollectorRegistry.GetCollectors();
            foreach (var collector in collectors)
            {
                if (collector.CanHandle(collectorHwnd, activeClass, tracker.GetProcessName(collectorHwnd)))
                {
                    polledByCollector = true;
                    var focused = IntPtr.Zero;
                    var activeClassName = string.Empty;
                    try
                    {
                        var threadId = KeyboardNativeMethods.GetWindowThreadProcessId(collectorHwnd, out _);
                        var guiInfo = new KeyboardNativeMethods.GUITHREADINFO();
                        guiInfo.cbSize = System.Runtime.InteropServices.Marshal.SizeOf(guiInfo);
                        if (KeyboardNativeMethods.GetGUIThreadInfo(threadId, ref guiInfo) && guiInfo.hwndFocus != IntPtr.Zero)
                        {
                            focused = guiInfo.hwndFocus;
                            var sbActiveCls = new StringBuilder(256);
                            KeyboardNativeMethods.GetClassName(focused, sbActiveCls, sbActiveCls.Capacity);
                            activeClassName = sbActiveCls.ToString();
                        }
                    }
                    catch { }

                    if (focused == IntPtr.Zero) focused = collectorHwnd;

                    var activePath = RunOnStaWithTimeout(() => collector.TryGetPath(focused, activeClassName, collectorHwnd, activeClass, tracker.GetProcessName(collectorHwnd)), null, TimeSpan.FromSeconds(2));
                    if (!IsObservedWindowStillActive(collectorHwnd, tracker.ActiveHwnd)) return;
                    if (!string.IsNullOrEmpty(activePath))
                    {
                        if (activePath != tracker.LastPath)
                        {
                            tracker.UpdatePath(activePath, false);
                        }
                    }
                    else if (!string.IsNullOrEmpty(tracker.LastPath))
                    {
                        tracker.UpdatePath(string.Empty, false);
                    }
                    break;
                }
            }
        }

        if (!polledByCollector && tracker.ActiveInlineAdapter != null && tracker.ActiveHwnd != IntPtr.Zero)
        {
            var inlineHwnd = tracker.ActiveHwnd;
            var inlineAdapter = tracker.ActiveInlineAdapter;
            var activePath = RunOnStaWithTimeout(() => inlineAdapter.GetSearchScope(inlineHwnd), null, TimeSpan.FromSeconds(2));
            if (!IsObservedWindowStillActive(inlineHwnd, tracker.ActiveHwnd)) return;
            if (!string.IsNullOrEmpty(activePath))
            {
                if (activePath != tracker.LastPath)
                {
                    tracker.UpdatePath(activePath, false);
                }
            }
            else if (!string.IsNullOrEmpty(tracker.LastPath))
            {
                tracker.UpdatePath(string.Empty, false);
            }
        }
    }

    private static T RunOnStaWithTimeout<T>(Func<T> func, T fallback, TimeSpan timeout)
    {
        using var done = new ManualResetEventSlim(false);
        Exception? error = null;
        var result = fallback;
        var thread = new Thread(() =>
        {
            try { result = func(); }
            catch (Exception ex) { error = ex; }
            finally { done.Set(); }
        })
        {
            IsBackground = true,
            Name = "ExplorerPathPollSta"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        if (!done.Wait(timeout))
            Logger.Log("[ExplorerActivePathPoller] Adapter read timed out; continuing without path.", LogLevel.Warn);
        else if (error != null)
            Logger.Log($"[ExplorerActivePathPoller] Adapter read failed: {error.Message}", LogLevel.Warn);

        return result;
    }

    internal static bool IsObservedWindowStillActive(IntPtr observedHwnd, IntPtr activeHwnd) =>
        observedHwnd != IntPtr.Zero && observedHwnd == activeHwnd;
}
