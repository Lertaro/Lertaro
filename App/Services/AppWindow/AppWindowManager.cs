using System.Windows;
using Lertaro.App.Views.QuickSearchWindow.Helpers;
using Lertaro.Core;

namespace Lertaro.App.Services.AppWindow;

public static class AppWindowManager
{
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

    // lertaro://settings/entry/<index> (see UriRouter) -- jumps straight to one specific setting
    // (section + tab + row highlight), not just its section. index into SettingsSearchIndex.Entries via
    // SettingsWindow.JumpToEntry, which validates it and no-ops on an out-of-range value.
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

    public static void ShowSearchWindow()
    {
        if (System.Windows.Application.Current == null) return;

        System.Windows.Application.Current.Dispatcher.Invoke(() => ShowSearchWindowCore());
    }

    /// <summary>
    /// Toggles the full SearchWindow for the global hotkey when "open full panel by default" is on:
    /// closes the visible full window if one is up, otherwise shows (or restores) the full window.
    /// </summary>
    public static void ToggleSearchWindow()
    {
        if (System.Windows.Application.Current == null) return;

        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            var visible = System.Windows.Application.Current.Windows.OfType<SearchWindow>().FirstOrDefault(w => w.IsVisible);
            if (visible != null)
            {
                visible.Close();
                return;
            }

            ShowSearchWindowCore();
        });
    }

    private static void ShowSearchWindowCore()
    {
        if (_searchWindow == null)
        {
            _searchWindow = UserSettings.Load().MainWindow.SingleInstance
                ? System.Windows.Application.Current.Windows.OfType<SearchWindow>().FirstOrDefault()
                : null;
            _searchWindow ??= new SearchWindow();
            _searchWindow.Closed += (_, _) => _searchWindow = null;
        }

        ShowAndActivateSearchWindow(_searchWindow);
    }

    // "Show more" is the only route that normally creates additional full windows. When the user
    // opts into one-instance mode, reuse any existing full window instead and carry its query forward.
    public static void ShowSearchWindowFromQuick(string query, bool restorePreview)
    {
        if (System.Windows.Application.Current == null) return;

        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            var current = System.Windows.Application.Current;
            var existing = UserSettings.Load().MainWindow.SingleInstance
                ? current.Windows.OfType<SearchWindow>().FirstOrDefault()
                : null;
            if (existing == null)
            {
                ShowAndActivateSearchWindow(new SearchWindow(query, restorePreview));
                return;
            }

            existing.SearchTextBox.Text = query;
            existing.SearchTextBox.SelectionStart = query.Length;
            ShowAndActivateSearchWindow(existing);
        });
    }

    private static void ShowAndActivateSearchWindow(SearchWindow window)
    {
        // Mirrors QuickSearchWindowController.ShowWindow's pre-show sequence: shell overlays are
        // dismissed while they still truthfully hold the foreground, and power throttling is lifted
        // before the first frame paints. Unlike the quick window, the full window is not permanently
        // topmost in XAML, so the false->true reassert below is what makes a hotkey summon surface
        // above other windows (auto topmost); the icon middle-click toggles it back off.
        ShellOverlayDismissHelper.DismissOverlayIfForeground();
        PowerThrottlingHelper.WindowShowing(window.PowerWindowId);

        // Auto-topmost on popup, same reassert the quick window does; the icon indicator follows it.
        window.Topmost = false;
        window.Topmost = true;
        window.SearchBox.IsStayOpen = true;

        if (!window.IsVisible)
            window.Show();
        if (window.WindowState == WindowState.Minimized)
            window.WindowState = WindowState.Normal;
        window.Activate();
        window.FocusSearch();
        // Session-start hook for the full window -- mirrors QuickSearchViewModel.
        ViewModels.Search.SearchReachabilityGate.BeginSession();
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
