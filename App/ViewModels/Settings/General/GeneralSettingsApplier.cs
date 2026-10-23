using Lertaro.App.Services;
using Lertaro.App.Services.Everything;
using Lertaro.Core;
using Lertaro.Core.Services.Search;
using Lertaro.Core.Wire;

namespace Lertaro.App.ViewModels.Settings.General;

/// <summary>
/// Split out purely to keep GeneralSettingsViewModel under the repo's per-file line limit.
/// This helper encapsulates the staged application and side-effect dispatch for general settings.
/// </summary>
internal static class GeneralSettingsApplier
{
    public static void Apply(
        GeneralSettingsViewModel vm,
        UserSettings userSettings,
        bool startWithWindows,
        bool autoCheckUpdates,
        bool autoSilentUpdate,
        bool enableHardwareAcceleration,
        bool enableFuzzyMatch,
        bool orFirstPrecedence,
        bool enableQuickSearchClipboardAutoFill,
        bool keepSearchText,
        bool enableEverythingIpc,
        bool enableRankingDetailsExport,
        bool showOpenedFoldersInInlineSearch,
        int inlineSearchWindowWidthPercent,
        int inlineSearchWindowMaxRows,
        int inlineSearchDialogMaxRows,
        bool hideTrayIcon,
        bool openFoldersInNewExplorerTabs,
        string globalTokenPrefix,
        string logLevel)
    {
        var logLevelChanged = userSettings.LogLevel != logLevel;
        var everythingIpcChanged = userSettings.EnableEverythingIpc != enableEverythingIpc;

        userSettings.StartWithWindows = startWithWindows;
        userSettings.AutoCheckUpdates = autoCheckUpdates;
        if (vm.IsUserAdmin)
            userSettings.AutoSilentUpdate = autoSilentUpdate;
        userSettings.EnableHardwareAcceleration = enableHardwareAcceleration;
        userSettings.EnableFuzzyMatch = enableFuzzyMatch;
        userSettings.OrFirstPrecedence = orFirstPrecedence;
        userSettings.EnableQuickSearchClipboardAutoFill = enableQuickSearchClipboardAutoFill;
        // Stored under SearchWindow because both windows read it (the quick one on hide, the full one on
        // Escape), but it is a system-wide search behaviour, so its row lives on the System tab.
        userSettings.SearchWindow.KeepSearchText = keepSearchText;
        SearchContext.DefaultFuzzyMatchEnabled = enableFuzzyMatch;
        // This process matches outside the search pipeline too (plugin catalog, favorites, shell-menu
        // filtering, highlighting), which only ever sees the process-wide value -- see SearchContext.
        SearchContext.DefaultAndFirstPrecedence = !orFirstPrecedence;
        userSettings.EnableEverythingIpc = enableEverythingIpc;
        // Read on the next quick-window search, so the export action appears/disappears without a restart.
        userSettings.EnableRankingDetailsExport = enableRankingDetailsExport;
        userSettings.ShowOpenedFoldersInInlineSearch = showOpenedFoldersInInlineSearch;
        userSettings.InlineSearchWindowWidthPercent = ToStoredInlineCardWidthPercent(inlineSearchWindowWidthPercent);
        userSettings.InlineSearchWindowMaxRows = inlineSearchWindowMaxRows;
        userSettings.InlineSearchDialogMaxRows = inlineSearchDialogMaxRows;
        userSettings.HideTrayIcon = hideTrayIcon;
        userSettings.DefaultFileManager.OpenFoldersInNewExplorerTabs = openFoldersInNewExplorerTabs;
        userSettings.GlobalTokenPrefix = string.IsNullOrWhiteSpace(globalTokenPrefix) ? ":" : globalTokenPrefix;
        userSettings.LogLevel = logLevel;
        // The third-party-file-manager fields are owned by their own sub-VM now; see vm.FileManager.Save
        // below rather than a copy of them threaded through this signature.

        StartupManager.SetEnabled(startWithWindows);
        (System.Windows.Application.Current.MainWindow as QuickSearchWindow)?.ApplyTrayIconVisibility(hideTrayIcon);
        Logger.MinimumLevel = SettingsOptionGenerator.ParseLogLevel(logLevel);
        if (logLevelChanged)
        {
            App.HookClient?.SendMessage(new IpcMessage { Id = IpcMessageId.ReloadSettings });
        }

        if (everythingIpcChanged)
        {
            if (enableEverythingIpc)
                EverythingServiceBootstrapper.Start(new SearchService());
            else
                EverythingServiceBootstrapper.Stop();
        }

        vm.Layout.Save();
        vm.PreviewWindow.Save();
        vm.MainWindow.Save();
        vm.FileManager.Save();
        vm.QuickNavigationOrder.Save();
        vm.ResultTypeOrder.Save();
        vm.SidebarGroupOrder.Save();
        vm.ColumnOrder.Save();
        vm.ActionMenuGroupOrder.Save();
        vm.FilePreviewProviderOrder.Save();
        vm.ThumbnailProviderOrder.Save();

        // SettingsViewModel persists all pages together and handles failure before notifying the Hook.
    }

    /// <summary>Stores the inline card's width percent, keeping the default unset rather than as a number.</summary>
    /// <remarks>
    /// null is what tells the card's placement (InlineCardMetrics.ResolveDockedWidth) to take the screen floor
    /// an untouched card has always had; a stored 60 would read as an explicit choice and lose it.
    /// </remarks>
    internal static int? ToStoredInlineCardWidthPercent(int percent) =>
        percent == UiMetrics.DefaultInlineCardWidthPercent ? null : percent;
}
