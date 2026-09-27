using System.Text;
using Lertaro.PluginSdk.Registries;
using Lertaro.Core.Hook.InlineSearch;
namespace Lertaro.Core.Hook;

internal sealed class ExplorerActivePathPoller : IDisposable
{
    // How long a burst has to hold still before the poller runs once for the whole of it. Short enough to
    // be imperceptible on the occasions a burst really did change the path, long enough that the signals it
    // now collapses -- a window being dragged or resized (measured at roughly 200 EVENT_OBJECT_LOCATIONCHANGE
    // a second while resizing Total Commander), and a target application's own tooltip/focus chatter --
    // produce one poll rather than hundreds. See PollsImmediately for why everything but a foreground change
    // comes through here.
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

    // How long one speculative ask may hold things: half the poll gap, so a retry cannot be the reason the
    // next event waits, and it gives up on the lock rather than queueing behind a read already in flight.
    // Shared with ExplorerTracker's re-ask of a dialog another process claimed, so the two processes hold a
    // speculative read for the same length of time.
    private const int RetryLockWaitMs = 50;
    internal const int RetryReadBudgetMs = 100;

    private readonly ExplorerWindowClassifier _classifier;
    private readonly QuietPeriodScheduler _scheduler;
    private readonly ExplorerHostReadFloor _readFloor = new();
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

    public void Poll(ExplorerTracker tracker, uint eventType, IntPtr eventHwnd)
    {
        _tracker = tracker;

        // Every WinEvent in the session used to reach this method with its hwnd never consulted, so a
        // tooltip appearing in ANY application bought a full path read for the tracked window -- and for a
        // host like XYplorer that read is a script round trip on its own UI thread, which dismisses the
        // popup the user is waiting for.
        //
        // Scope, honestly: this gate only removes UNRELATED windows' events. Measured after it, XYplorer's
        // info tip still lived just 201-235ms per attempt, because the pointer moving through the list
        // raises name-change and focus events from the host's own panes -- legitimately about the tracked
        // window, and correctly passing this test. Demand gating (ExplorerHostReadFloor) is what stopped
        // reading on those.
        var tracked = tracker.ActiveHwnd;
        if (!RelatesToTrackedWindow(eventType, eventHwnd,
                ExplorerNativeHooks.GetAncestor(eventHwnd, ExplorerNativeHooks.GA_ROOTOWNER),
                tracked, ExplorerNativeHooks.GetForegroundWindow()))
        {
            return;
        }

        if (PollsImmediately(eventType))
        {
            // A window switch is demand, and demand has to outlive the gap between asking and running: the
            // scheduler may fire on the timer thread after other events have arrived, so "what the last event
            // was" could not be carried as a field without losing this one.
            _readFloor.RequestForegroundRead();
            _scheduler.RunNow();
            return;
        }

        // A window moving or resizing says nothing about the tracked window's path most of the time, but it
        // does occasionally carry one (measured for Explorer, Total Commander and file dialogs alike), so it
        // cannot just be dropped. Wait for the movement to stop and poll once for the whole burst.
        _scheduler.RunWhenQuiet();
    }

    // Whether one event is about the window whose path this poller exists to follow, at all.
    internal static bool RelatesToTrackedWindow(uint eventType, IntPtr eventHwnd, IntPtr rootOwner, IntPtr trackedHwnd, IntPtr foreground)
    {
        if (eventType == ExplorerNativeHooks.EVENT_SYSTEM_FOREGROUND) return true;

        // Nothing is tracked yet: this is how a window that was never claimed gets found at all (a dialog
        // that builds its child controls after taking the foreground -- see UnclaimedDialogRetryLimit's own
        // comment, written after the Rimage report). A session with no tracked window must keep listening
        // to every event, or that window stays unclaimed forever.
        if (trackedHwnd == IntPtr.Zero) return true;

        // The tracked window itself, or anything living inside it -- a pane, a tab strip, a file dialog's
        // address bar -- all report through the root owner, and a folder change inside the host is exactly
        // such a child event.
        if (eventHwnd == trackedHwnd || rootOwner == trackedHwnd) return true;

        // The foreground window's own events: it can differ from the tracked one the moment something else
        // took focus, which is precisely what a poll is meant to notice. A tooltip never gets here, since
        // tooltips are not activated and their root owner is themselves.
        return foreground != IntPtr.Zero && eventHwnd == foreground;
    }

    // Whether one WinEvent is allowed to poll on the spot, or has to wait for the burst to settle first.
    //
    // Only a real foreground change earns an immediate poll. Everything else used to get one, and
    // EVENT_OBJECT_FOCUS / EVENT_OBJECT_NAMECHANGE are precisely what a target application manufactures
    // because the user moved the mouse inside it -- a tooltip popping up raises both, and the poll's answer
    // for a file-manager host is a synchronous read into that same application.
    //
    // What this buys, stated accurately: collapsing per-event polls cut the read frequency by orders of
    // magnitude, but the tip kept dying afterwards at 171-235ms -- exactly the settle period, since every
    // burst still ended in that same read. Demand gating is what fixed it; this rule earns its keep on cost,
    // keeping a resize at ~200 events a second from becoming 200 identification passes a second.
    //
    // Settling rather than dropping is deliberate: a tab switch inside the same window is a burst of these
    // very events, and the path change it carries still has to be picked up once the burst ends.
    internal static bool PollsImmediately(uint eventType) =>
        eventType == ExplorerNativeHooks.EVENT_SYSTEM_FOREGROUND;

    public void Dispose() => _scheduler.Dispose();

    // Demand sources, forwarded to the floor. Both are called from threads that must not stall on this one:
    // the low-level keyboard hook (RequestHostPathRead) and the App's IPC link (SetInlineWindowOnScreen).
    public void RequestHostPathRead() => _readFloor.RequestRead();

    // Closing the window drops steady demand AND any pending one-shot: with nothing on screen, a request left
    // over from the keystroke that summoned it would buy a read for nobody.
    public void SetInlineWindowOnScreen(bool onScreen)
    {
        if (onScreen) _readFloor.CardOnScreen = true;
        else _readFloor.ClearCardOnScreen();
    }

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

        // Tighter than what a real foreground change gets, and willing to skip a contended lock: a retry is
        // speculative, so the honest answer is to ask again shortly rather than to hold the thread that reads
        // every window in the session. ExplorerStaInvoker's own abandoned-thread cap then turns a wedged
        // target into fast failures instead of a queue of waits.
        _classifier.CheckActiveWindow(foreground, RetryLockWaitMs, RetryReadBudgetMs);

        Logger.Log(
            $"[ExplorerTracker] Unclaimed #32770 retry for 0x{foreground:x}: "
            + (tracker.IsActiveWindowDialog ? "claimed." : $"still unclaimed, {(_askedLeft > 0 ? _askedLeft + " left" : "budget spent")}."),
            LogLevel.Debug);

        // Arm the next attempt rather than waiting for an event that a settled window stops producing. Once
        // the dialog has been claimed this stops on its own, and a dialog that never finishes building spends
        // at most UnclaimedDialogRetryLimit attempts on it.
        if (!tracker.IsActiveWindowDialog) _scheduler.RunWhenQuiet();
    }

    private void PollCore(ExplorerTracker tracker)
    {
        // Reads answer to demand and to one interval per window: pointer movement inside a host is neither,
        // and the read itself is what cancels the host's transient UI. See ExplorerHostReadFloor.
        var nowTicks = Environment.TickCount64;
        var hostReadAllowed = _readFloor.AllowsRead(tracker.ActiveHwnd, nowTicks);

        var currentFg = ExplorerNativeHooks.GetForegroundWindow();
        if (currentFg != IntPtr.Zero && currentFg != tracker.ActiveHwnd)
        {
            var sbClass = new StringBuilder(256);
            ExplorerNativeHooks.GetClassName(currentFg, sbClass, sbClass.Capacity);
            var className = sbClass.ToString();
            var processName = tracker.GetProcessName(currentFg);
            // Bounded like the classifier's own reads: these adapters reach into the target process, and this
            // runs on the WinEvent thread whenever an event asked for an immediate poll.
            var worthIdentifying = ExplorerStaInvoker.RunOnStaWithTimeout(
                () => FileDialogAdapterRegistry.GetMatchingAdapter(currentFg, className, processName) != null
                    || InlineSearchAdapterRegistry.GetMatchingAdapter(currentFg, className, processName) != null
                    || ActivePathCollectorRegistry.GetCollectors()
                        .Any(collector => collector.CanHandle(currentFg, className, processName)),
                false,
                TimeSpan.FromMilliseconds(ExplorerWindowClassifier.DefaultPluginTimeoutMs));
            if (worthIdentifying)
            {
                _classifier.CheckActiveWindow(currentFg);
            }
        }

        RetryUnclaimedDialog(tracker, currentFg);

        if (hostReadAllowed && tracker.IsActiveWindowDialog && tracker.ActiveHwnd != IntPtr.Zero && tracker.ActiveAdapter != null)
        {
            var dialogHwnd = tracker.ActiveHwnd;
            var dialogAdapter = tracker.ActiveAdapter;
            _readFloor.NoteRead(dialogHwnd, nowTicks);
            var activePath = ExplorerStaInvoker.RunOnStaWithTimeout(() => dialogAdapter.GetCurrentPath(dialogHwnd), null, TimeSpan.FromSeconds(2));
            if (!IsObservedWindowStillActive(dialogHwnd, tracker.ActiveHwnd)) return;
            if (!string.IsNullOrEmpty(activePath) && activePath != tracker.LastPath)
            {
                tracker.UpdatePath(activePath, false);
            }
        }

        var polledByCollector = false;
        if (hostReadAllowed && tracker.ActiveHwnd != IntPtr.Zero && tracker.ActiveInlineAdapter == null)
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
                    _readFloor.NoteRead(collectorHwnd, nowTicks);
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

        if (hostReadAllowed && !polledByCollector && tracker.ActiveInlineAdapter != null && tracker.ActiveHwnd != IntPtr.Zero)
        {
            var inlineHwnd = tracker.ActiveHwnd;
            var inlineAdapter = tracker.ActiveInlineAdapter;
            _readFloor.NoteRead(inlineHwnd, nowTicks);
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
