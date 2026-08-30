using System.Windows;
using Lertaro.App.ViewModels.Search;

namespace Lertaro.App.Helpers;

// An open quick search window holds two views of the favorites/quick-launch settings that only
// refresh on their own schedule: the launch panel rebuilds when the window is next SHOWN
// (QuickSearchWindowShowSupport.ShowWindow), and the result list only reflects whatever those
// settings said at dispatch time. Both stay stale when the user edits them in Settings while the
// window is already open. This pushes the saved data into those windows immediately, so a user
// action in Settings is reflected in every open search surface without waiting for the next
// show or keystroke -- settings saves are rare, so the one extra re-run per open window costs
// nothing noticeable.
internal static class OpenSearchWindowRefresher
{
    // Must run on the UI thread (SettingsViewModel.Apply's caller): it walks Application.Current.Windows.
    public static void AfterSettingsSaved()
    {
        foreach (Window window in System.Windows.Application.Current.Windows)
        {
            if (window.DataContext is not QuickSearchViewModel quickVm)
                continue;

            quickVm.RefreshLaunchSources();

            // Re-run the live query so favorite/history-weighted rows re-read the saved settings.
            // An empty box has nothing to re-run: its empty state contains no favorites content.
            var currentQuery = quickVm.SearchQuery;
            if (!string.IsNullOrWhiteSpace(currentQuery))
                quickVm.Search.PerformSearch(currentQuery);
        }
    }
}
