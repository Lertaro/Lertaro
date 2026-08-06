using System.Windows;

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

        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            if (_searchWindow == null)
            {
                _searchWindow = new SearchWindow();
                _searchWindow.Closed += (_, _) => _searchWindow = null;
            }

            if (!_searchWindow.IsVisible)
                _searchWindow.Show();

            if (_searchWindow.WindowState == WindowState.Minimized)
                _searchWindow.WindowState = WindowState.Normal;

            _searchWindow.Activate();
            // Session-start hook for the full window -- mirrors QuickSearchViewModel.
            // EnsureServiceMonitoringActive, which is the same hook for the Quick/Inline windows.
            ViewModels.Search.SearchReachabilityGate.BeginSession();
        });
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
