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
        bool enableEverythingIpc,
        bool hideTrayIcon,
        bool openFoldersInNewExplorerTabs,
        string globalTokenPrefix,
        string logLevel,
        bool defaultFileManagerEnabled,
        string defaultFileManagerPath,
        string defaultFileManagerParameter)
    {
        var logLevelChanged = userSettings.LogLevel != logLevel;
        var everythingIpcChanged = userSettings.EnableEverythingIpc != enableEverythingIpc;

        userSettings.StartWithWindows = startWithWindows;
        userSettings.AutoCheckUpdates = autoCheckUpdates;
        if (vm.IsUserAdmin)
            userSettings.AutoSilentUpdate = autoSilentUpdate;
        userSettings.EnableHardwareAcceleration = enableHardwareAcceleration;
        userSettings.EnableFuzzyMatch = enableFuzzyMatch;
        SearchContext.DefaultFuzzyMatchEnabled = enableFuzzyMatch;
        userSettings.EnableEverythingIpc = enableEverythingIpc;
        userSettings.HideTrayIcon = hideTrayIcon;
        userSettings.DefaultFileManager.OpenFoldersInNewExplorerTabs = openFoldersInNewExplorerTabs;
        userSettings.GlobalTokenPrefix = string.IsNullOrWhiteSpace(globalTokenPrefix) ? ":" : globalTokenPrefix;
        userSettings.LogLevel = logLevel;
        userSettings.DefaultFileManager.Enabled = defaultFileManagerEnabled;
        userSettings.DefaultFileManager.Path = defaultFileManagerPath;
        userSettings.DefaultFileManager.Parameter = defaultFileManagerParameter;

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
        vm.QuickNavigationOrder.Save();
        vm.ResultTypeOrder.Save();
        vm.SidebarGroupOrder.Save();
        vm.ColumnOrder.Save();
        vm.ActionMenuGroupOrder.Save();
        vm.FilePreviewProviderOrder.Save();
        vm.ThumbnailProviderOrder.Save();

        userSettings.Save();
    }
}
