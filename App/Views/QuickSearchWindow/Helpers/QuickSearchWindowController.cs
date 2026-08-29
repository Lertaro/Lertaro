using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Lertaro.App.Services;
using Lertaro.Core;

using Lertaro.App.Services.ShellIcons;
using Lertaro.App.Services.Theme;
using Lertaro.App.ViewModels.Search;
using Lertaro.Core.Wire;
namespace Lertaro.App.Views.QuickSearchWindow.Helpers;

public class QuickSearchWindowController
{
    private readonly Lertaro.App.QuickSearchWindow _window;

    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    private const int SW_RESTORE = 9;

    private IntPtr _lastActiveHwnd = IntPtr.Zero;
    private readonly QuickSearchWindowPositioner _positioner;
    private readonly QuickSearchWindowForegroundWatcher _foregroundWatcher;

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

    // useAltTapBypass: see HookCommandHandler's ForceForeground case -- callers backed by very recent real
    // input on the Hook's own thread already (e.g. Quick Navigation's own mouse click) should pass false.
    public static void ForceForeground(IntPtr hwnd, bool useAltTapBypass = true)
    {
        if (hwnd == IntPtr.Zero) return;
        ShowWindow(hwnd, SW_RESTORE);

        // Used to skip the IPC round-trip when GetForegroundWindow() already reported hwnd as
        // foreground, on the assumption that Show()/Activate() already did the whole job. That
        // assumption isn't always safe (e.g. a still-open shell overlay like the Start Menu can make
        // Windows report our window as foreground without real per-thread keyboard focus having
        // actually moved -- see ShellOverlayDismissHelper.DismissOverlayIfForeground(), which handles
        // that case earlier in ShowWindow()). Always send it regardless; redoing an already-correct
        // foreground/focus state is cheap and harmless.
        App.HookClient?.SendMessage(new IpcMessage
        {
            Id = IpcMessageId.ForceForeground,
            Hwnd = hwnd.ToInt64(),
            BoolVal = useAltTapBypass
        });
    }

    public QuickSearchWindowController(Lertaro.App.QuickSearchWindow window)
    {
        _window = window;
        _positioner = new QuickSearchWindowPositioner(window, () => _lastActiveHwnd);
        _foregroundWatcher = new QuickSearchWindowForegroundWatcher(window, () => HideOnFocusLoss());
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

    public void ShowWindow(string? initialQuery = null)
    {
        _visibilityOpToken++;

        // As early as possible, before Show() -- Windows' Power Throttling operates at the process
        // scheduling level, so lifting it only after this window has already started painting would miss
        // the very first frame the user is waiting on. See PowerThrottlingHelper's own comment.
        PowerThrottlingHelper.WindowShowing("quick");
        // Cancels a trim that is armed but has not fired: emptying the working set moments before a
        // summon is strictly worse than never emptying it.
        IdleWorkingSetTrimmer.WindowShowing();

        // Must run before anything below touches this window (Show()/Activate()/ForceForeground):
        // once any of those runs, GetForegroundWindow() starts reporting THIS window as foreground
        // (shell light-dismiss overlays don't compete for activation the normal way), so this is the
        // last point where it still reflects the real, uncontaminated state.
        ShellOverlayDismissHelper.DismissOverlayIfForeground();

        _lastActiveHwnd = GetForegroundWindow();
        if (_lastActiveHwnd != IntPtr.Zero)
        {
            GetWindowThreadProcessId(_lastActiveHwnd, out var activePid);
            if (activePid == (uint)Environment.ProcessId) _lastActiveHwnd = IntPtr.Zero;
        }

        _window.ViewModel.IsInlineSearchContext = false;
        App.HideInlineSearch();
        QuickLookManager.Instance.Reset();
        _window.ViewModel.RefreshLaunchItems();

        InlineSearchManager.Instance.KeyboardHook.IsQuickSearchWindowVisible = true;
        InlineSearchManager.Instance.KeyboardHook.Stop();
        _window.ViewModel.EnsureServiceMonitoringActive();

        _window.ViewModel.SearchQuery = initialQuery ?? string.Empty;
        _window.ViewModel.RefreshEmptyState();
        _window.ViewModel.RefreshLayoutSettings();
        // UpdateLayout() first so everything the calls above may have just changed actually
        // measures/arranges before ApplyResultsLayoutImmediate reads it -- then apply the results height synchronously too, rather than leaving it to the
        // normal Send-priority-deferred QueueResultsLayoutUpdate. Both run while the window is still
        // hidden, so the window's first visible frame (at Show() below) already lands at the correct size
        // instead of showing at whatever Height this persistent window was last left at (a different tab,
        // a longer results list, ...) and only snapping to the real size after a visible post-show flash.
        _window.UpdateLayout();
        _window.ApplyResultsLayoutImmediate();
        _window.Topmost = false;
        _window.Topmost = true;
        // Position before Show() (not after, as before) so the window's first painted frame already
        // lands at the real target -- Show()-then-PositionWindow() used to render one frame at wherever
        // this persistent window was last left (a different monitor entirely, if that's where the user
        // last summoned it) before snapping to the correct spot, an unconditional flash-then-jump on
        // every summon rather than just while on a single monitor. PositionWindow() itself doesn't need
        // the window's actual layout/HWND for anything (its width comes from settings, not ActualWidth,
        // and its DPI now comes from the target monitor, not this window's own CompositionTarget), so
        // moving it earlier is safe.
        PositionWindow();

        // Fade in from 0 rather than popping in fully opaque -- same technique (and target opacity)
        // ThemeManager already uses for its own theme-swap fade, animating window.Content rather than
        // the Window itself so a translucent theme's WindowOpacity (see WindowEffectHelper) stays the
        // ceiling instead of always fading to a flat 1.0.
        var fadeContent = _window.Content as UIElement;
        fadeContent?.BeginAnimation(UIElement.OpacityProperty, null);
        fadeContent?.Opacity = 0;

        _window.Show();
        _window.WindowState = WindowState.Normal;

        if (fadeContent != null)
        {
            var targetOpacity = ThemeManager.Instance.ActiveTheme?.WindowOpacity ?? 1.0;
            var fadeIn = new DoubleAnimation(targetOpacity, (Duration)System.Windows.Application.Current.FindResource("DurationWindowFadeIn"))
            {
                EasingFunction = System.Windows.Application.Current.TryFindResource("EaseOutCubic") as IEasingFunction
            };
            fadeContent.BeginAnimation(UIElement.OpacityProperty, fadeIn);
        }

        _foregroundWatcher.Start();

        ActivateAndFocus();
    }

    // ForceForeground's SetForegroundWindow call -- whether it succeeds locally or has to round-trip
    // through the elevated Hook process's IPC -- doesn't complete synchronously with the call that
    // requested it. TxtSearch.Focus() used to fire after a single fixed-priority dispatcher hop, a
    // guess at "enough time has probably passed" that could still land before the OS actually handed
    // this window real keyboard focus, silently dropping any keys the user typed in that gap right
    // after invoking the hotkey (see issue #121). Poll the real OS state instead: 10ms ticks, capped
    // at 200ms so a case where foreground genuinely never arrives (something else is holding it,
    // blocked by Windows' foreground-lock rules) still ends in focusing the search box rather than
    // leaving it silently unfocused forever.
    private void FocusSearchBoxWhenForeground(IntPtr hwnd)
    {
        var deadline = Environment.TickCount64 + 200;
        var timer = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(10) };
        timer.Tick += (s, _) =>
        {
            var isForeground = hwnd == IntPtr.Zero || GetForegroundWindow() == hwnd;
            if (!isForeground && Environment.TickCount64 < deadline)
                return;

            timer.Stop();
            _window.TxtSearch.Focus();
            System.Windows.Input.Keyboard.Focus(_window.TxtSearch);
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
    private void ActivateAndFocus() => _window.Dispatcher.BeginInvoke(new Action(() =>
    {
        var hwnd = new WindowInteropHelper(_window).Handle;
        if (hwnd != IntPtr.Zero) ForceForeground(hwnd);

        _window.Activate();
        _window.Focus();

        FocusSearchBoxWhenForeground(hwnd);
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

            if (restoreFocus && !_suppressNextRestore && _lastActiveHwnd != IntPtr.Zero) SetForegroundWindow(_lastActiveHwnd);
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
