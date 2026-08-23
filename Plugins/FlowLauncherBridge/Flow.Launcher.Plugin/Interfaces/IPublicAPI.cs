using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using Flow.Launcher.Plugin.SharedModels;

namespace Flow.Launcher.Plugin;

/// <summary>
/// Public APIs provided by Flow.Launcher host for plugin invocation.
/// Direct interface method declarations are required for IL binary compatibility with third-party plugins.
/// </summary>
public interface IPublicAPI
{
    void ChangeQuery(string query, bool requery = false);
    void ReQuery(bool reselect = true);
    void BackToQueryResults();
    void RestartApp();
    void ShellRun(string cmd, string filename = "cmd.exe");
    void CopyToClipboard(string text, bool directCopy = false, bool showDefaultNotification = true);
    void SaveAppAllSettings();
    void SavePluginSettings();
    Task ReloadAllPluginData();
    void CheckForNewUpdate();
    void ShowMsgError(string title, string subTitle = "");
    void ShowMsgErrorWithButton(string title, string buttonText, Action buttonAction, string subTitle = "");
    void ShowMainWindow();
    void FocusQueryTextBox();
    void HideMainWindow();
    bool IsMainWindowVisible();
    void ToggleGameMode();
    void SetGameMode(bool value);
    bool IsGameModeOn();
    void RegisterGlobalKeyboardCallback(Func<int, int, SpecialKeyState, bool> callback);
    void RemoveGlobalKeyboardCallback(Func<int, int, SpecialKeyState, bool> callback);
    event VisibilityChangedEventHandler VisibilityChanged;

    void ShowMsg(string title, string subTitle = "", string iconPath = "");
    void ShowMsg(string title, string subTitle, string iconPath, bool useMainWindowAsOwner = true);
    void ShowMsgWithButton(string title, string buttonText, Action buttonAction, string subTitle = "", string iconPath = "");
    void ShowMsgWithButton(string title, string buttonText, Action buttonAction, string subTitle, string iconPath, bool useMainWindowAsOwner = true);
    MessageBoxResult ShowMsgBox(string messageBoxText, string caption = "", MessageBoxButton button = MessageBoxButton.OK, MessageBoxImage icon = MessageBoxImage.None, MessageBoxResult defaultResult = MessageBoxResult.OK);
    Task ShowProgressBoxAsync(string caption, Func<Action<double>, Task> reportProgressAsync, Action? cancelProgress = null);
    void StartLoadingBar();
    void StopLoadingBar();

    T LoadSettingJsonStorage<T>() where T : new();
    void SaveSettingJsonStorage<T>() where T : new();
    void SavePluginCaches();
    Task<T> LoadCacheBinaryStorageAsync<T>(string cacheName, string cacheDirectory, T defaultData) where T : new();
    Task SaveCacheBinaryStorageAsync<T>(string cacheName, string cacheDirectory) where T : new();

    void OpenSettingDialog();
    bool OpenPluginSettingsWindow(string pluginId);
    string GetTranslation(string key);
    List<PluginPair> GetAllPlugins();
    List<PluginPair> GetAllInitializedPlugins(bool includeFailed);
    void OpenDirectory(string DirectoryPath, string? FileNameOrFilePath = null);
    void OpenWebUrl(Uri url, bool? inPrivate = null);
    void OpenWebUrl(string url, bool? inPrivate = null);
    void OpenUrl(Uri url, bool? inPrivate = null);
    void OpenUrl(string url, bool? inPrivate = null);
    void OpenAppUri(Uri appUri);
    void OpenAppUri(string appUri);
    List<ThemeData> GetAvailableThemes();
    ThemeData GetCurrentTheme();
    bool SetCurrentTheme(ThemeData theme);

    MatchResult FuzzySearch(string query, string stringToCompare);
    Task<string> HttpGetStringAsync(string url, CancellationToken token = default);
    Task<Stream> HttpGetStreamAsync(string url, CancellationToken token = default);
    Task HttpDownloadAsync(string url, string filePath, Action<double>? reportProgress = null, CancellationToken token = default);
    void AddActionKeyword(string pluginId, string newActionKeyword);
    void RemoveActionKeyword(string pluginId, string oldActionKeyword);
    bool ActionKeywordAssigned(string actionKeyword);
    void LogDebug(string className, string message, [CallerMemberName] string methodName = "");
    void LogInfo(string className, string message, [CallerMemberName] string methodName = "");
    void LogWarn(string className, string message, [CallerMemberName] string methodName = "");
    void LogError(string className, string message, [CallerMemberName] string methodName = "");
    void LogException(string className, string message, Exception e, [CallerMemberName] string methodName = "");
    ValueTask<ImageSource?> LoadImageAsync(string path, bool loadFullImage = false, bool cacheImage = true);
    Task<bool> UpdatePluginManifestAsync(bool usePrimaryUrlOnly = false, CancellationToken token = default);
    IReadOnlyList<UserPlugin> GetPluginManifest();
    bool PluginModified(string id);
    Task<bool> UpdatePluginAsync(PluginMetadata pluginMetadata, UserPlugin plugin, string zipFilePath);
    bool InstallPlugin(UserPlugin plugin, string zipFilePath);
    Task<bool> UninstallPluginAsync(PluginMetadata pluginMetadata, bool removePluginSettings = false);
    long StopwatchLogDebug(string className, string message, Action action, [CallerMemberName] string methodName = "");
    Task<long> StopwatchLogDebugAsync(string className, string message, Func<Task> action, [CallerMemberName] string methodName = "");
    long StopwatchLogInfo(string className, string message, Action action, [CallerMemberName] string methodName = "");
    Task<long> StopwatchLogInfoAsync(string className, string message, Func<Task> action, [CallerMemberName] string methodName = "");
    bool IsApplicationDarkTheme();
    event ActualApplicationThemeChangedEventHandler ActualApplicationThemeChanged;
    string GetDataDirectory();
    string GetLogDirectory();
    event EventHandler StringMatcherBehaviorChanged;
}
