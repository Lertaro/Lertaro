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

    // How many times a common dialog that nothing claimed is asked again, and the poller's own 200ms quiet
    // period is the gap between asks.
    //
    // The reason this exists: a dialog whose child controls are built AFTER it takes the foreground answers
    // "not a file dialog" to the single classification it gets, and nothing ever asks again -- re-identification
    // is gated on the foreground window having changed, and a dialog just sitting there produces no more
    // events. Rimage's 添加文件夹 is the report that led here: measured once it had settled, that very window
    // is a plain #32770 with a Breadcrumb Parent and an Edit #1152, which every adapter would claim, yet no
    // card appeared until the user moved the focus away and back -- the one thing that manufactures a fresh
    // foreground event. Asked by a timer instead of by an event, it is claimed as soon as it settles.
    internal const int UnclaimedDialogRetryLimit = 12;

    private readonly ExplorerWindowClassifier _classifier;
    private readonly QuietPeriodScheduler _scheduler;
    private ExplorerTracker? _tracker;

    // The budget belongs to the window, not to the poller: one dialog that never becomes interesting must not
    // spend the retries of the next one.
    internal static int BudgetFor(IntPtr foreground, IntPtr askedFor, int askedLeft) =>
        foreground == IntPtr.Zero || foreground != askedFor ? UnclaimedDialogRetryLimit : askedLeft;

    private IntPtr _askedFor;
    private int _askedLeft = UnclaimedDialogRetryLimit;

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

    private void RetryUnclaimedDialog(ExplorerTracker tracker, IntPtr foreground)
    {
        if (tracker.IsActiveWindowDialog || !ExplorerNativeHooks.IsCommonDialogClass(foreground))
        {
            _askedFor = IntPtr.Zero;
            _askedLeft = UnclaimedDialogRetryLimit;
            return;
        }

        _askedLeft = BudgetFor(foreground, _askedFor, _askedLeft);
        _askedFor = foreground;
        if (_askedLeft <= 0) return;
        _askedLeft--;

        _classifier.CheckActiveWindow(foreground);

        // Arm the next attempt rather than waiting for an event that a settled window stops producing. Once
        // the dialog has been claimed this stops on its own, and a dialog that never finishes building spends
        // at most UnclaimedDialogRetryLimit attempts on it.
        if (!tracker.IsActiveWindowDialog) _scheduler.RunWhenQuiet();
    }

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

        RetryUnclaimedDialog(tracker, currentFg);

        if (tracker.IsActiveWindowDialog && tracker.ActiveHwnd != IntPtr.Zero && tracker.ActiveAdapter != null)
        {
            var dialogHwnd = tracker.ActiveHwnd;
            var dialogAdapter = tracker.ActiveAdapter;
            var activePath = ExplorerStaInvoker.RunOnStaWithTimeout(() => dialogAdapter.GetCurrentPath(dialogHwnd), null, TimeSpan.FromSeconds(2));
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

                    var activePath = ExplorerStaInvoker.RunOnStaWithTimeout(() => collector.TryGetPath(focused, activeClassName, collectorHwnd, activeClass, tracker.GetProcessName(collectorHwnd)), null, TimeSpan.FromSeconds(2));
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
            var activePath = ExplorerStaInvoker.RunOnStaWithTimeout(() => inlineAdapter.GetSearchScope(inlineHwnd), null, TimeSpan.FromSeconds(2));
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

    internal static bool IsObservedWindowStillActive(IntPtr observedHwnd, IntPtr activeHwnd) =>
        observedHwnd != IntPtr.Zero && observedHwnd == activeHwnd;
}
