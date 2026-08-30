using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Lertaro.App.Services;
using Lertaro.App.Services.ShellIcons;
using Lertaro.Core;
using Lertaro.App.ViewModels.Search;
namespace Lertaro.App.Views.QuickSearchWindow.Helpers;

public class QuickSearchWindowController
{
    private readonly Lertaro.App.QuickSearchWindow _window;
    private readonly QuickSearchWindowShowSupport _showSupport;
    private IntPtr _lastActiveHwnd = IntPtr.Zero;
    private readonly QuickSearchWindowPositioner _positioner;
    private readonly QuickSearchWindowForegroundWatcher _foregroundWatcher;

    internal Lertaro.App.QuickSearchWindow Window => _window;
    internal QuickSearchWindowForegroundWatcher ForegroundWatcher => _foregroundWatcher;
    internal IntPtr LastActiveHwnd { get => _lastActiveHwnd; set => _lastActiveHwnd = value; }
    internal int VisibilityOperationToken { get => _visibilityOpToken; set => _visibilityOpToken = value; }
    // Bumped by every ShowWindow()/HideWindow() call. HideWindow()'s actual Hide() is deferred behind
    // a fade-out (see its own comment), so a rapid Show() right after a Hide() needs a way to tell that
    // deferred continuation "a newer call already superseded you" -- otherwise the pending continuation
    // would still fire and hide the window moments after the user just re-summoned it.
    private int _visibilityOpToken;
    // Set by a caller that's about to intentionally move focus to an external window itself as part of
    // executing a result (e.g. WindowSwitcher's "activatewindow:" action -- see PluginActionExecutor).
    // HideWindow's actual restore-focus step can be reached from THREE independent triggers -- this
    // class's own HideWindow call, QuickSearchWindow.Window_Deactivated's safety net, and
    // QuickSearchWindowForegroundWatcher's global foreground hook -- and whichever one happens to win
    // the FinishHide race would otherwise restore focus to _lastActiveHwnd (whatever was foreground
    // before this window was ever shown), undoing the freshly-activated target a beat later. Guarding
    // FinishHide's own restore step once, here, covers all three without needing each caller to agree
    // on which of them will actually fire first. Consumed (and cleared) the next time FinishHide runs,
    // regardless of which trigger got there.
    private bool _suppressNextRestore;
    public void SuppressNextRestore() => _suppressNextRestore = true;
    // useAltTapBypass is used by callers backed by recent input on the Hook thread.
    public static void ForceForeground(IntPtr hwnd, bool useAltTapBypass = true) => QuickSearchWindowNative.ForceForeground(hwnd, useAltTapBypass);

    public QuickSearchWindowController(Lertaro.App.QuickSearchWindow window)
    {
        _window = window;
        _positioner = new QuickSearchWindowPositioner(window, () => _lastActiveHwnd);
        _foregroundWatcher = new QuickSearchWindowForegroundWatcher(window, () => HideOnFocusLoss());
        _showSupport = new QuickSearchWindowShowSupport(this);
    }

    public void PositionWindow() => _positioner.PositionWindow();
    public void SaveWindowPosition() => _positioner.SaveWindowPosition();

    // Wired to the search box's status icon right-click -- clears the saved position and immediately
    // re-centers the window using the same fallback PositionWindow already falls back to when there's
    // no saved position (or it's off-screen).
    public void ResetPosition() => _positioner.ResetPosition();

    public void ToggleVisibility() => _window.Dispatcher.Invoke(() =>
    {
        switch (DetermineToggleAction(_window.IsVisible, _window.IsActive, _window.WindowState, UserSettings.Load().SearchWindow.ReopenAsFullWindowOnRepeatHotkey, _stayOpen))
        {
            case ToggleAction.Show: ShowWindow(); break;
            case ToggleAction.Focus: FocusWindow(); break;
            case ToggleAction.ReopenAsFullWindow: ReopenAsFullWindow(); break;
            default: HideWindow(); break;
        }
    });

    internal enum ToggleAction { Show, Hide, ReopenAsFullWindow, Focus }

    // Pulled out of ToggleVisibility() so the decision tree (as opposed to the real Show/Hide/reopen
    // I/O each branch performs) can be unit tested without a live window.
    internal static ToggleAction DetermineToggleAction(bool isVisible, bool isActive, WindowState windowState, bool reopenAsFullWindowSetting, bool stayOpen)
    {
        if (!isVisible || windowState == WindowState.Minimized) return ToggleAction.Show;

        // Visible but not focused, because Stay Open is what kept it up when focus left. The hotkey means
        // "bring me back to it" here, not "put it away" -- the window is on screen and the user is looking
        // at it, so hiding it is the opposite of what pressing the summon key asked for. Deliberately
        // narrowed to Stay Open rather than applied to any unfocused-but-visible state: the hotkey is also
        // how the window gets dismissed, and a state that stopped it dismissing would be worse than this
        // gap.
        if (stayOpen && !isActive) return ToggleAction.Focus;

        return reopenAsFullWindowSetting ? ToggleAction.ReopenAsFullWindow : ToggleAction.Hide;
    }

    // Opens the full SearchWindow carrying over the quick window's current query -- the same
    // "__SHOW_MORE__" path as the quick window's own Ctrl+F/"Open More" expand action -- then hides
    // this window without restoring focus to whatever was active before it (the full window is about
    // to take foreground instead).
    private void ReopenAsFullWindow()
    {
        var query = SearchResultTypePriority.StripLeadingTrigger(_window.ViewModel.SearchQuery);
        FileExecutor.OpenFileOrFolder("__SHOW_MORE__", query, () => HideWindow(restoreFocus: false));
    }

    public void ShowWindow(string? initialQuery = null) => _showSupport.ShowWindow(initialQuery);

    // ForceForeground's SetForegroundWindow call -- whether it succeeds locally or has to round-trip
    // through the elevated Hook process's IPC -- doesn't complete synchronously with the call that
    // requested it. TxtSearch.Focus() used to fire after a single fixed-priority dispatcher hop, a
    // guess at "enough time has probably passed" that could still land before the OS actually handed
    // this window real keyboard focus, silently dropping any keys the user typed in that gap right
    // after invoking the hotkey (see issue #121). Poll the real OS state instead: 10ms ticks, capped
    // at 200ms so a case where foreground genuinely never arrives (something else is holding it,
    // blocked by Windows' foreground-lock rules) still ends in focusing the search box rather than
    // leaving it silently unfocused forever.
    private void FocusSearchBoxWhenForeground(IntPtr hwnd, bool selectSearchText)
    {
        var deadline = Environment.TickCount64 + 200;
        var timer = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(10) };
        timer.Tick += (s, _) =>
        {
            var isForeground = hwnd == IntPtr.Zero || QuickSearchWindowNative.GetForegroundWindow() == hwnd;
            if (!isForeground && Environment.TickCount64 < deadline)
                return;

            timer.Stop();
            _window.TxtSearch.Focus();
            System.Windows.Input.Keyboard.Focus(_window.TxtSearch);
            if (selectSearchText) _window.TxtSearch.SelectAll();
        };
        timer.Start();
    }

    // Set by the Stay Open hotkey, cleared by the next real hide (see FinishHide), so it only ever
    // covers the summon it was pressed in.
    private bool _stayOpen;

    /// <summary>Whether this summon has been asked not to auto-hide when it loses focus.</summary>
    public bool IsStayOpen => _stayOpen;

    /// <summary>Raised when <see cref="IsStayOpen"/> changes, so the view can show it.</summary>
    public event Action<bool>? StayOpenChanged;

    /// <summary>
    /// Toggles "do not auto-hide on focus loss" for the current summon.
    /// </summary>
    /// <remarks>
    /// For assembling a query out of text copied from several other windows: every switch away would
    /// otherwise hide the window, and hiding clears the query (see FinishHide), so a half-built search
    /// was lost each time. Requested as #197.
    /// </remarks>
    public void ToggleStayOpen()
    {
        _stayOpen = !_stayOpen;
        StayOpenChanged?.Invoke(_stayOpen);
    }

    /// <summary>
    /// Brings the window back to the foreground without touching anything it is holding.
    /// </summary>
    /// <remarks>
    /// Deliberately NOT ShowWindow: that resets the summon, and its very first act is to assign
    /// SearchQuery, which would wipe the half-assembled query this whole feature exists to protect. All
    /// that is wanted here is the activation half.
    /// </remarks>
    public void FocusWindow()
    {
        // First, before anything here touches the window, for the reason ShowWindow gives at its own call:
        // once Show/Activate/ForceForeground has run, GetForegroundWindow() reports THIS window and the
        // check can no longer see the overlay. Missing it here meant pressing the summon key while the
        // Start Menu was open refocused this window and left the Start Menu sitting there -- which the
        // normal summon path never did, because it runs this.
        ShellOverlayDismissHelper.DismissOverlayIfForeground();

        PowerThrottlingHelper.WindowShowing("quick");
        IdleWorkingSetTrimmer.WindowShowing();
        _window.Topmost = false;
        _window.Topmost = true;
        ActivateAndFocus();
    }

    // The activation half of a summon, shared with ShowWindow so the two cannot drift apart.
    internal void ActivateAndFocus() => ActivateAndFocus(false);
    internal void ActivateAndFocus(bool selectSearchText) => _window.Dispatcher.BeginInvoke(new Action(() =>
    {
        var hwnd = new WindowInteropHelper(_window).Handle;
        if (hwnd != IntPtr.Zero) QuickSearchWindowNative.ForceForeground(hwnd);

        _window.Activate();
        _window.Focus();

        FocusSearchBoxWhenForeground(hwnd, selectSearchText);
    }), DispatcherPriority.Input);

    /// <summary>
    /// The hide that happens because focus went elsewhere, as opposed to one the user asked for.
    /// </summary>
    /// <remarks>
    /// Separate entry point rather than a check inside HideWindow, because HideWindow is also how every
    /// DELIBERATE hide runs -- opening a result, running an action or a plugin action, the global hotkey
    /// toggling the window away. Those must still hide while Stay Open is on, or pressing Enter would
    /// leave the window sitting there over the file it just opened. Only the two focus-loss callers come
    /// through here: Window_Deactivated's safety net and QuickSearchWindowForegroundWatcher's global
    /// hook. They are separate paths that both had to be covered -- a flag honoured by only one of them
    /// leaks, which is exactly how the preview window's own hide bugs went.
    /// </remarks>
    public void HideOnFocusLoss(bool restoreFocus = true)
    {
        if (_stayOpen)
            return;
        HideWindow(restoreFocus);
    }

    public void HideWindow(bool restoreFocus = true)
    {
        var opToken = ++_visibilityOpToken;

        _foregroundWatcher.Stop();
        if (_window.MenuPresenter != null && _window.MenuPresenter.IsInActionsMode)
        {
            _window.MenuPresenter.ExitActionsMode();
        }

        _window.ViewModel.Monitor.StopStatusTimer();

        // Must happen before the SearchQuery reset below: clearing the query resets SelectedResult to
        // the startup panel's first item, which synchronously fires LstResults.SelectionChanged and
        // (with _userWantsPreview still true) would make QuickLookManager jump the still-open preview
        // window to that unrelated file instead of closing alongside this window.
        QuickLookManager.Instance.Reset();

        try { KeywordHistoryStore.Record(_window.ViewModel.SearchQuery); } catch { }
        _window.KeywordHistoryController.Reset();

        // Everything below this point (the SearchQuery reset onward) reads as the window's "closed"
        // state, so it's deferred behind a fade-out of whatever is CURRENTLY on screen -- fading out
        // first, then resetting the content, means the window dismisses showing what the user was just
        // looking at instead of jump-cutting to the empty/startup-panel state a beat before it vanishes.
        void FinishHide()
        {
            // A newer ShowWindow()/HideWindow() call already superseded this one (e.g. the user
            // re-summoned the window mid fade-out) -- don't hide out from under them.
            if (opToken != _visibilityOpToken) return;

            // SearchQuery's own setter already runs PerformSearch("") when this actually changes the query
            // (clearing/replacing results the normal way). Explicitly wiping Search.Results here on top of
            // that used to erase the startup panel's own still-valid results/tabs the moment the box was
            // already empty (nothing "changes" so the setter is a no-op) -- meaning next time the window
            // showed, there was nothing left to display while the panel's async refetch ran, which is what
            // produced the empty/loading flash ShowWindow's RefreshEmptyState() was supposed to avoid.
            // Dies with the summon it belonged to, so the next one starts with the normal behaviour.
            if (_stayOpen)
            {
                _stayOpen = false;
                StayOpenChanged?.Invoke(false);
            }

            _window.ViewModel.SearchQuery = string.Empty;

            _window.UpdateLayout();
            _window.Hide();
            PowerThrottlingHelper.WindowHidden("quick");

            InlineSearchManager.Instance.KeyboardHook.IsQuickSearchWindowVisible = false;
            InlineSearchManager.Instance.KeyboardHook.Start();

            if (restoreFocus && !_suppressNextRestore && _lastActiveHwnd != IntPtr.Zero) QuickSearchWindowNative.SetForegroundWindow(_lastActiveHwnd);
            _suppressNextRestore = false;
            _lastActiveHwnd = IntPtr.Zero;

            Task.Run(async () =>
            {
                await Task.Delay(100);
                // These two genuinely free memory, so they still run on every hide.
                try { ShellIconHelper.ClearCache(); } catch { }
                try { PathCacheMaintenance.ClearAllPathCaches(); } catch { }
            });

            // Trimming the working set frees nothing -- it only evicts pages the next summon has to
            // fault straight back in, which measured at ~17MB and 70% of a summon's time every single
            // time, and at 4.7 seconds once when the machine was under an index rebuild's memory and
            // disk pressure. Deferred until the window has been left alone; see IdleWorkingSetTrimGate.
            IdleWorkingSetTrimmer.WindowHidden();
        }

        if (_window.Content is UIElement fadeContent)
        {
            var fadeOutDuration = (Duration)System.Windows.Application.Current.FindResource("DurationFast");
            var fadeOut = new DoubleAnimation(0.0, fadeOutDuration)
            {
                EasingFunction = System.Windows.Application.Current.TryFindResource("EaseOutCubic") as IEasingFunction
            };
            fadeContent.BeginAnimation(UIElement.OpacityProperty, fadeOut);

            Task.Run(async () =>
            {
                await Task.Delay(fadeOutDuration.TimeSpan);
                _window.Dispatcher.Invoke(FinishHide);
            });
        }
        else
        {
            FinishHide();
        }
    }
}
