using System.Windows.Threading;
using Lertaro.Core.Wire;

namespace Lertaro.App.Views.InlineSearchWindow.Helpers;

// Owns "keep the host file manager's own selection in step with the inline result list": the IPC that
// tells the active adapter which path is highlighted, the suppression/coalescing state that keeps a
// streamed result refresh from firing that IPC once per keystroke, and the focus reclaim some adapters
// need afterwards.
//
// Split out of InlineSearchWindowInputHandler (composition, not a partial class) to keep that file under
// the repository's per-file line limit. It has no state of its own beyond this one concern and always
// operates on the one window it is handed.
internal sealed class InlineExplorerSelectionSync
{
    private readonly Lertaro.App.InlineSearchWindow _window;
    private bool _refreshInProgress;
    private int _refreshQueued;
    private DispatcherTimer? _reclaimFocusTimer;

    public InlineExplorerSelectionSync(Lertaro.App.InlineSearchWindow window) => _window = window;

    // Sends the current selection to the host, unless a result-set refresh is mid-burst (the coalesced
    // callback will send once it settles -- see BeginResultRefresh).
    public void Sync()
    {
        if (_refreshInProgress)
            return;
        Send();
    }

    // An explicit user navigation (arrow key / next-item hotkey) must reach the host NOW even while a
    // refresh burst has automatic sync held back: that hold exists to collapse the auto-select pass's
    // per-row sends into one (see BeginResultRefresh), not to swallow a deliberate key press.
    public void SyncAfterNavigation()
    {
        if (_refreshInProgress)
            Send();
    }

    // Holds automatic sync back for the duration of one result-set refresh and schedules ONE coalesced
    // sync for when it settles.
    //
    // ReconcileTo fires one CollectionChanged per differing row (Replace/Add/Remove), not one Reset --
    // every one of those reaches this method, so without a coalescing guard a single result-set update
    // queued 20-40+ dispatcher callbacks, each re-running the auto-select loop and a real IPC SendMessage
    // -- individually cheap (sub-millisecond) but the sheer count was the actual source of the typing lag.
    //
    // The settle callback is queued at Input, NOT ContextIdle. At ContextIdle -- the dispatcher's lowest
    // priority -- a busy result stream starved it indefinitely, so the hold never lifted: the host never
    // followed the search while the user was typing (live syncing's whole point), and an arrow key pressed
    // during that window was swallowed because the flag was still set. Input outranks rendering, and a
    // pending user input always gets serviced, so the hold lifts and the host catches up promptly.
    public void BeginResultRefresh(Action autoSelectIfNeeded)
    {
        _refreshInProgress = true;
        if (Interlocked.Exchange(ref _refreshQueued, 1) == 1)
            return;

        _window.Dispatcher.BeginInvoke(new Action(() =>
        {
            Interlocked.Exchange(ref _refreshQueued, 0);
            _refreshInProgress = false;
            autoSelectIfNeeded();
            Send();
        }), DispatcherPriority.Input);
    }

    private void Send()
    {
        var target = ResolveMirrorTarget();
        if (target is not { } resolved)
            return;

        var tracker = _window.Manager.ExplorerTracker;
        if (tracker.ActiveInlineAdapter == null || tracker.ActiveHwnd == IntPtr.Zero)
            return;

        // Trailing separator marks a directory, same convention ExecuteItem's own IPC call already
        // uses (see InlineAdapterIpcCoordinator.NormalizePath) -- an adapter's OnSelectionChanged runs
        // in the Hook process, which can't reliably re-derive this itself (Directory.Exists on a
        // mapped network drive silently fails when the Hook is elevated into a different logon
        // session), so it has to travel with the path instead of being recomputed on the other end.
        var path = resolved.IsDir && !resolved.Path.EndsWith('\\') ? resolved.Path + "\\" : resolved.Path;
        App.HookClient?.SendMessage(new IpcMessage
        {
            Id = IpcMessageId.InlineSelectionChanged,
            Hwnd = tracker.ActiveHwnd.ToInt64(),
            StringVal1 = path
        });

        // Some adapters' OnSelectionChanged can activate the host file manager as a side effect
        // (XYplorer's goto/SelectItems) or as a hard requirement (TC's CD command needs to be
        // foreground to act at all) -- either way it steals keyboard focus mid-typing, eating even
        // Backspace. How long that takes (or whether it happens at all) is entirely host-specific, so
        // each adapter tunes its own SelectionSyncFocusReclaimDelayMs rather than sharing one
        // process-wide constant; 0 (the default) means this adapter never does this, so skip
        // scheduling entirely.
        var reclaimDelayMs = tracker.ActiveInlineAdapter.SelectionSyncFocusReclaimDelayMs;
        if (reclaimDelayMs > 0)
            ScheduleFocusReclaim(reclaimDelayMs);
    }

    // What the host should highlight for the current list state -- see InlineResultTargetResolver, which
    // owns the decision (and its reasoning) as pure functions.
    private (string Path, bool IsDir)? ResolveMirrorTarget()
    {
        var rows = _window.LstResults.Items.OfType<AppSearchResult>().ToList();
        var selectedIndex = _window.LstResults.SelectedItem is not AppSearchResult selected ? -1 : rows.IndexOf(selected);
        return InlineResultTargetResolver.ResolveMirrorTarget(rows, selectedIndex, _window.ViewModel.SearchScope);
    }

    // Reset (not just started) on every call so rapid typing only reclaims once, after the last
    // selection change actually settles. Uses ActivateAndFocusSearchBox (Activate() + AttachThreadInput),
    // not a plain SearchTextBox.Focus() -- once another process's window has taken OS-level foreground,
    // WPF's own focus calls alone can't pull real keyboard input back without also re-activating this
    // window.
    private void ScheduleFocusReclaim(int delayMs)
    {
        if (_reclaimFocusTimer == null)
        {
            _reclaimFocusTimer = new DispatcherTimer();
            _reclaimFocusTimer.Tick += (s, e) =>
            {
                _reclaimFocusTimer!.Stop();
                if (_window.IsVisible) _window.ActivateAndFocusSearchBox();
            };
        }
        _reclaimFocusTimer.Stop();
        _reclaimFocusTimer.Interval = TimeSpan.FromMilliseconds(delayMs);
        _reclaimFocusTimer.Start();
    }
}
