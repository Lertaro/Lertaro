using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Lertaro.App.Helpers;
using Lertaro.App.Views.QuickSearchWindow.Helpers;
using Lertaro.Core;

namespace Lertaro.App.Services.AppWindow;

public static class AppWindowManager
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    private static SettingsWindow? _settingsWindow;
    private static SearchWindow? _searchWindow;

    public static void ShowSettingsWindow(string? targetSection = null)
    {
        // Application.Current goes null once the app has started (or finished) shutting down --
        // reachable when a caller queued this before exit and only actually runs afterward (e.g. the
        // startup update-check's "new version found" prompt is a modal ShowDialog, so the user can
        // still click Exit on the tray icon while it's up; by the time ShowDialog returns and this
        // runs, Shutdown may have already torn Application.Current down). Nothing useful to show at
        // that point -- just no-op instead of crashing on Application.Current.Dispatcher.
        if (System.Windows.Application.Current == null) return;

        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            if (_settingsWindow == null)
            {
                _settingsWindow = new SettingsWindow();
                _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            }

            // Select the target section before the window becomes visible/restored -- doing it after
            // Show() let the window briefly render whatever section was already selected (the default,
            // or whatever was left over from a previous open) before flipping to the requested one,
            // which read as a jarring flash instead of opening straight into the right place.
            if (!string.IsNullOrEmpty(targetSection))
            {
                _settingsWindow.SelectSection(targetSection);
            }

            if (!_settingsWindow.IsVisible)
                _settingsWindow.Show();

            if (_settingsWindow.WindowState == WindowState.Minimized)
                _settingsWindow.WindowState = WindowState.Normal;

            _settingsWindow.Activate();
            _settingsWindow.FocusSearchBox();
        });
    }

    // Jumps straight to one specific setting (section + tab + row highlight), not just its section.
    // The index comes from the host's searchable settings entries and is validated by the window.
    public static void ShowSettingsWindowEntry(int entryIndex)
    {
        if (System.Windows.Application.Current == null) return;

        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            if (_settingsWindow == null)
            {
                _settingsWindow = new SettingsWindow();
                _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            }

            // Same before-Show ordering as ShowSettingsWindow, for the same reason (see its own
            // comment) -- JumpToEntry's own highlight/scroll step is separately deferred internally
            // (ActivateSearchResult), so it still lands correctly once layout has actually happened.
            _settingsWindow.JumpToEntry(entryIndex);

            if (!_settingsWindow.IsVisible)
                _settingsWindow.Show();

            if (_settingsWindow.WindowState == WindowState.Minimized)
                _settingsWindow.WindowState = WindowState.Normal;

            _settingsWindow.Activate();
        });
    }

    public static void ShowQuickLaunchItemEditor(string path)
        => QuickLaunchItemEditor.Show(path);

    public static void ShowSearchWindow()
    {
        if (System.Windows.Application.Current == null) return;

        System.Windows.Application.Current.Dispatcher.Invoke(() => ShowSearchWindowCore(bringToFront: false));
    }

    /// <summary>
    /// The global summon hotkey's decision, shared by every route that can have opened the full window.
    /// </summary>
    /// <remarks>
    /// A visible full window is handled ahead of the "open full panel by default" setting on purpose: the
    /// full window is reachable with that setting off (the quick window's expand, "show more", and
    /// ReopenAsFullWindowOnRepeatHotkey), and the summon key must mean the same thing there. Handled here
    /// rather than at the call site because routing it at the call site let that third press fall through
    /// to the QUICK window's own visibility toggle -- the full window stayed on screen, so the key read as
    /// doing nothing.
    /// </remarks>
    public static void HandleGlobalSummonHotkey()
    {
        if (System.Windows.Application.Current == null) return;

        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            var settings = UserSettings.Load();
            var visible = System.Windows.Application.Current.Windows.OfType<SearchWindow>().FirstOrDefault(w => w.IsVisible);
            switch (DetermineSearchWindowHotkeyAction(visible != null, visible?.IsActive == true, settings.MainWindow.CloseOnRepeatHotkey))
            {
                case SearchWindowHotkeyAction.CloseFullWindow:
                    // Opt-in alternative to the return below: the user treats the full window as an
                    // ordinary window, so the key that summoned it also puts it away.
                    visible!.Close();
                    return;
                case SearchWindowHotkeyAction.ReturnToQuickSearch:
                    // Already focused on the full window: the second press is the user asking to go back
                    // where they came from, carrying the query over.
                    visible!.ReturnToQuickSearch();
                    return;
                case SearchWindowHotkeyAction.BringToFront:
                    BringSearchWindowToFront(visible!);
                    return;
            }

            if (settings.Hotkeys.OpenFullWindowByDefault)
                ShowSearchWindowCore(bringToFront: true);
            else
                (System.Windows.Application.Current.MainWindow as QuickSearchWindow)?.ToggleVisibility();
        });
    }

    internal enum SearchWindowHotkeyAction { NoFullWindowOnScreen, BringToFront, ReturnToQuickSearch, CloseFullWindow }

    // Pulled out of the I/O above so the hotkey's decision tree can be unit tested without a live window,
    // mirroring QuickSearchWindowController.DetermineToggleAction. NoFullWindowOnScreen is deliberately not
    // "show the full window": whether the key then opens the full or the quick window is the "open full
    // panel by default" setting's business, so that branch is left to the caller.
    //
    // closeOnRepeatHotkey only distinguishes the two things a focused press can mean. A visible but
    // UNFOCUSED full window stays "bring it to front" either way: there the key means "come back to it",
    // and closing a window the user is trying to return to would be the opposite of what they asked.
    internal static SearchWindowHotkeyAction DetermineSearchWindowHotkeyAction(bool isVisible, bool isActive, bool closeOnRepeatHotkey) =>
        !isVisible ? SearchWindowHotkeyAction.NoFullWindowOnScreen
        : !isActive ? SearchWindowHotkeyAction.BringToFront
        : closeOnRepeatHotkey ? SearchWindowHotkeyAction.CloseFullWindow
        : SearchWindowHotkeyAction.ReturnToQuickSearch;

    private static void ShowSearchWindowCore(bool bringToFront)
    {
        if (_searchWindow == null)
        {
            _searchWindow = UserSettings.Load().MainWindow.SingleInstance
                ? System.Windows.Application.Current.Windows.OfType<SearchWindow>().FirstOrDefault()
                : null;
            _searchWindow ??= new SearchWindow();
            _searchWindow.Closed += (_, _) => _searchWindow = null;
        }

        ShowAndActivateSearchWindow(_searchWindow, bringToFront);
    }

    // "Show more" is the only route that normally creates additional full windows. When the user
    // opts into one-instance mode, reuse any existing full window instead and carry its query forward.
    public static void ShowSearchWindowFromQuick(string query, bool restorePreview)
    {
        if (System.Windows.Application.Current == null) return;
        query = SearchTextPasteFormatter.FormatForSearch(query) ?? string.Empty;

        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            var current = System.Windows.Application.Current;
            var seedRows = ReadQuickSearchRows(query);
            var existing = UserSettings.Load().MainWindow.SingleInstance
                ? current.Windows.OfType<SearchWindow>().FirstOrDefault()
                : null;
            if (existing == null)
            {
                ShowAndActivateSearchWindow(new SearchWindow(query, restorePreview, seedRows), bringToFront: false);
                return;
            }

            if (seedRows != null)
                existing.HandOffQuickSearchResults(seedRows);
            existing.SearchTextBox.Text = query;
            if (SearchInputHelper.ShouldSelectCarriedText(query, UserSettings.Load().SearchWindow.KeepSearchText))
                existing.SearchTextBox.SelectAll();
            else
                existing.SearchTextBox.SelectionStart = query.Length;
            ShowAndActivateSearchWindow(existing, bringToFront: false);
        });
    }

    /// <summary>
    /// The rows the quick window is showing for exactly this query, as far as the full window can list them.
    /// </summary>
    /// <remarks>
    /// "Show more" carries the TEXT only, so the full window used to open on an empty grid and fill in when
    /// its own -- much wider, unbounded -- search first answered. On a big index that gap is seconds of
    /// nothing where the user left a list they were already reading. Null (the ordinary case for any other
    /// route in) just means no hand-off, and the window behaves as it always did.
    /// </remarks>
    private static IReadOnlyList<AppSearchResult>? ReadQuickSearchRows(string query)
    {
        if (System.Windows.Application.Current.MainWindow is not QuickSearchWindow quick)
            return null;

        var search = quick.ViewModel.Search;
        // The rows have to belong to the text being carried over, or this window opens showing an answer to
        // a query nobody asked. Callers hand over the box text with the result-type trigger already
        // stripped, so strip it the same way instead of comparing against what the box holds.
        var carried = search.SearchQuery ?? string.Empty;
        if (!string.Equals(ViewModels.Search.SearchResultTypePriority.StripLeadingTrigger(carried), query, StringComparison.Ordinal))
            return null;

        return OnlyListableRows(search.Results);
    }

    /// <summary>
    /// The subset of another window's rows this one can actually list, or null when there are none.
    /// </summary>
    internal static IReadOnlyList<AppSearchResult>? OnlyListableRows(IEnumerable<AppSearchResult> rows)
    {
        List<AppSearchResult>? kept = null;
        foreach (var row in rows)
        {
            // Real files and folders only: "show more" and "no results" are the quick window's own UI (and
            // arrive with a "__" sentinel path), applications have no place in this grid, and a plugin row
            // carries no path, size or type for its columns to show.
            if (row.ResultKind != "File" || row.FullPath.Length == 0 || row.FullPath[0] == '_')
                continue;
            (kept ??= new List<AppSearchResult>()).Add(row);
        }

        return kept;
    }

    private static void ShowAndActivateSearchWindow(SearchWindow window, bool bringToFront)
    {
        // Mirrors QuickSearchWindowController.ShowWindow's pre-show sequence: shell overlays are
        // dismissed while they still truthfully hold the foreground, and power throttling is lifted
        // before the first frame paints. The full window is never made automatically topmost: the
        // global hotkey gets one foreground handoff, while a deliberate logo middle-click remains the
        // only way to change its persistent Topmost state.
        ShellOverlayDismissHelper.DismissOverlayIfForeground();
        PowerThrottlingHelper.WindowShowing(window.PowerWindowId);
        if (bringToFront)
            IdleWorkingSetTrimmer.WindowShowing();

        if (!window.IsVisible)
            window.Show();
        if (window.WindowState == WindowState.Minimized)
            window.WindowState = WindowState.Normal;
        if (bringToFront)
            ActivateAndFocusSearchWindow(window);
        else
        {
            window.Activate();
            window.FocusSearch();
        }
        // Session-start hook for the full window -- mirrors QuickSearchViewModel.
        ViewModels.Search.SearchReachabilityGate.BeginSession();
    }

    // A visible but inactive full window is still on screen, so the global shortcut refocuses it. An
    // active full window is handled by HandleGlobalSummonHotkey as a return to the quick window instead.
    private static void BringSearchWindowToFront(SearchWindow window)
    {
        ShellOverlayDismissHelper.DismissOverlayIfForeground();
        PowerThrottlingHelper.WindowShowing(window.PowerWindowId);
        IdleWorkingSetTrimmer.WindowShowing();

        if (window.WindowState == WindowState.Minimized)
            window.WindowState = WindowState.Normal;

        ActivateAndFocusSearchWindow(window);
    }

    // Keep the complete window's foreground handoff identical to the quick window's activation half:
    // dispatch it at input priority, ask the hook service to cross the foreground-lock boundary, then
    // wait for Windows to report the window as foreground before focusing the search box. Topmost is
    // intentionally absent here because the global full-window shortcut must not leave auto-topmost on.
    private static void ActivateAndFocusSearchWindow(SearchWindow window) => window.Dispatcher.BeginInvoke(new Action(() =>
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd != IntPtr.Zero) QuickSearchWindowController.ForceForeground(hwnd);

        window.Activate();
        window.Focus();
        FocusSearchBoxWhenForeground(window, hwnd);
    }), DispatcherPriority.Input);

    // ForceForeground may complete asynchronously in the elevated hook service. Poll the actual OS
    // foreground state instead of guessing with a fixed dispatcher delay, so the first character typed
    // immediately after a summon is not lost to a still-pending focus transfer.
    private static void FocusSearchBoxWhenForeground(SearchWindow window, IntPtr hwnd)
    {
        var deadline = Environment.TickCount64 + 200;
        var timer = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(10) };
        timer.Tick += (s, _) =>
        {
            var isForeground = hwnd == IntPtr.Zero || GetForegroundWindow() == hwnd;
            if (!isForeground && Environment.TickCount64 < deadline)
                return;

            timer.Stop();
            window.SearchTextBox.Focus();
            Keyboard.Focus(window.SearchTextBox);
            if (SearchInputHelper.ShouldSelectCarriedText(window.SearchTextBox.Text, UserSettings.Load().SearchWindow.KeepSearchText))
                window.SearchTextBox.SelectAll();
        };
        timer.Start();
    }

    public static void CloseAllManagedWindows()
    {
        if (System.Windows.Application.Current == null) return;

        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            _settingsWindow?.Close();
            _settingsWindow = null;
            _searchWindow?.Close();
            _searchWindow = null;
        });
    }
}
