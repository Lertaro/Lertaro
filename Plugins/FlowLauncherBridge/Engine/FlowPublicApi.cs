using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using Flow.Launcher.Plugin;
using Flow.Launcher.Plugin.SharedModels;

namespace Lertaro.Plugins.FlowLauncherBridge.Engine;

/// <summary>
/// Bridge implementation of Flow.Launcher's IPublicAPI interface.
/// Connects Flow plugins to Lertaro runtime capabilities.
/// </summary>
public class FlowPublicApi : IPublicAPI
{
    private readonly PluginMetadata _metadata;
    private readonly FlowSettingsStorage _storage;
    private readonly Func<List<PluginPair>> _getPluginsFunc;
    private readonly Action<string, bool>? _changeQueryAction;
    private readonly Action<string, string>? _addActionKeywordAction;
    private readonly Action<string, string>? _removeActionKeywordAction;
    private readonly Func<string, bool>? _actionKeywordAssignedFunc;
    private readonly HttpClient _httpClient = new();

    public FlowPublicApi(
        PluginMetadata metadata,
        FlowSettingsStorage storage,
        Func<List<PluginPair>> getPluginsFunc,
        Action<string, bool>? changeQueryAction = null,
        Action<string, string>? addActionKeywordAction = null,
        Action<string, string>? removeActionKeywordAction = null,
        Func<string, bool>? actionKeywordAssignedFunc = null)
    {
        _metadata = metadata;
        _storage = storage;
        _getPluginsFunc = getPluginsFunc;
        _changeQueryAction = changeQueryAction;
        _addActionKeywordAction = addActionKeywordAction;
        _removeActionKeywordAction = removeActionKeywordAction;
        _actionKeywordAssignedFunc = actionKeywordAssignedFunc;
    }

    public event VisibilityChangedEventHandler? VisibilityChanged { add { } remove { } }
    public event ActualApplicationThemeChangedEventHandler? ActualApplicationThemeChanged { add { } remove { } }
    public event EventHandler? StringMatcherBehaviorChanged { add { } remove { } }

    public void ChangeQuery(string query, bool requery = false) => _changeQueryAction?.Invoke(query, requery);
    public void ReQuery(bool reselect = true) => ChangeQuery(string.Empty, true);
    public void BackToQueryResults() { }

    public void RestartApp()
    {
        var exePath = Process.GetCurrentProcess().MainModule?.FileName;
        if (!string.IsNullOrEmpty(exePath))
        {
            Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = true });
            Application.Current?.Shutdown();
        }
    }

    public void ShellRun(string cmd, string filename = "cmd.exe") => Process.Start(new ProcessStartInfo(filename, $"/c {cmd}") { CreateNoWindow = true, UseShellExecute = false });

    public void CopyToClipboard(string text, bool directCopy = false, bool showDefaultNotification = true)
    {
        if (string.IsNullOrEmpty(text))
            return;

        try
        {
            if (directCopy && (File.Exists(text) || Directory.Exists(text)))
            {
                var dropList = new System.Collections.Specialized.StringCollection { text };
                Clipboard.SetFileDropList(dropList);
            }
            else
            {
                Clipboard.SetText(text);
            }
        }
        catch
        {
            // Clipboard access can throw if occupied by another app
        }
    }

    public void SaveAppAllSettings() => _storage.SaveAll();
    public void SavePluginSettings() => _storage.SaveAll();
    public Task ReloadAllPluginData() => Task.CompletedTask;
    public void CheckForNewUpdate() { }

    public void ShowMsgError(string title, string subTitle = "") => ShowMsg(title, subTitle, string.Empty);
    public void ShowMsgErrorWithButton(string title, string buttonText, Action buttonAction, string subTitle = "") => ShowMsg(title, subTitle, string.Empty);
    public void ShowMainWindow() { }
    public void FocusQueryTextBox() { }
    public void HideMainWindow() { }
    public bool IsMainWindowVisible() => true;

    public void ToggleGameMode() { }
    public void SetGameMode(bool value) { }
    public bool IsGameModeOn() => false;
    public void RegisterGlobalKeyboardCallback(Func<int, int, SpecialKeyState, bool> callback) { }
    public void RemoveGlobalKeyboardCallback(Func<int, int, SpecialKeyState, bool> callback) { }

    public void ShowMsg(string title, string subTitle = "", string iconPath = "") => Debug.WriteLine($"[FlowPlugin:{_metadata.Name}] {title}: {subTitle}");

    public void ShowMsg(string title, string subTitle, string iconPath, bool useMainWindowAsOwner = true) => ShowMsg(title, subTitle, iconPath);
    public void ShowMsgWithButton(string title, string buttonText, Action buttonAction, string subTitle = "", string iconPath = "") => ShowMsg(title, subTitle, iconPath);
    public void ShowMsgWithButton(string title, string buttonText, Action buttonAction, string subTitle, string iconPath, bool useMainWindowAsOwner = true) => ShowMsg(title, subTitle, iconPath);

    public MessageBoxResult ShowMsgBox(string messageBoxText, string caption = "", MessageBoxButton button = MessageBoxButton.OK, MessageBoxImage icon = MessageBoxImage.None, MessageBoxResult defaultResult = MessageBoxResult.OK) => MessageBox.Show(messageBoxText, caption, button, icon, defaultResult);

    public Task ShowProgressBoxAsync(string caption, Func<Action<double>, Task> reportProgressAsync, Action? cancelProgress = null) => reportProgressAsync(_ => { });

    public void StartLoadingBar() { }
    public void StopLoadingBar() { }

    private string PluginSettingKey => !string.IsNullOrEmpty(_metadata.Name) ? _metadata.Name : _metadata.ID;

    public T LoadSettingJsonStorage<T>() where T : new() => _storage.LoadSetting<T>(PluginSettingKey);
    public void SaveSettingJsonStorage<T>() where T : new() => _storage.SaveSetting<T>(PluginSettingKey);
    public void SavePluginCaches() { }
    public Task<T> LoadCacheBinaryStorageAsync<T>(string cacheName, string cacheDirectory, T defaultData) where T : new() => Task.FromResult(defaultData);
    public Task SaveCacheBinaryStorageAsync<T>(string cacheName, string cacheDirectory) where T : new() => Task.CompletedTask;

    public void OpenSettingDialog() => OpenPluginSettingsWindow(_metadata.ID);

    public bool OpenPluginSettingsWindow(string pluginId)
    {
        var pair = _getPluginsFunc().FirstOrDefault(p => string.Equals(p.Metadata.ID, pluginId, StringComparison.OrdinalIgnoreCase));
        if (pair == null)
            return false;

        try
        {
            Process.Start(new ProcessStartInfo("lertaro://settings/page/Plugins") { UseShellExecute = true });
            return true;
        }
        catch
        {
            return false;
        }
    }
    public string GetTranslation(string key) => FlowPluginLanguageHelper.GetTranslation(key);
    public List<PluginPair> GetAllPlugins() => _getPluginsFunc();
    public List<PluginPair> GetAllInitializedPlugins(bool includeFailed) => _getPluginsFunc();

    public void OpenDirectory(string DirectoryPath, string? FileNameOrFilePath = null)
    {
        if (string.IsNullOrWhiteSpace(DirectoryPath))
            return;

        if (!string.IsNullOrWhiteSpace(FileNameOrFilePath) && File.Exists(FileNameOrFilePath))
            Process.Start("explorer.exe", $"/select,\"{FileNameOrFilePath}\"");
        else
            Process.Start("explorer.exe", $"\"{DirectoryPath}\"");
    }

    public void OpenWebUrl(Uri url, bool? inPrivate = null) => OpenWebUrl(url.ToString(), inPrivate);
    public void OpenWebUrl(string url, bool? inPrivate = null) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    public void OpenUrl(Uri url, bool? inPrivate = null) => OpenWebUrl(url, inPrivate);
    public void OpenUrl(string url, bool? inPrivate = null) => OpenWebUrl(url, inPrivate);
    public void OpenAppUri(Uri appUri) => OpenWebUrl(appUri.ToString());
    public void OpenAppUri(string appUri) => OpenWebUrl(appUri);

    public List<ThemeData> GetAvailableThemes() => [];
    public ThemeData GetCurrentTheme() => new("Default", string.Empty);
    public bool SetCurrentTheme(ThemeData theme) => true;

    public MatchResult FuzzySearch(string query, string stringToCompare)
    {
        if (string.IsNullOrEmpty(query) || string.IsNullOrEmpty(stringToCompare))
            return new MatchResult(false, SearchPrecisionScore.Regular);

        var match = stringToCompare.Contains(query, StringComparison.OrdinalIgnoreCase);
        return new MatchResult(match, SearchPrecisionScore.Regular, [], match ? 50 : 0);
    }

    public async Task<string> HttpGetStringAsync(string url, CancellationToken token = default) => await _httpClient.GetStringAsync(url, token);
    public async Task<Stream> HttpGetStreamAsync(string url, CancellationToken token = default) => await _httpClient.GetStreamAsync(url, token);

    public async Task HttpDownloadAsync(string url, string filePath, Action<double>? reportProgress = null, CancellationToken token = default)
    {
        var data = await _httpClient.GetByteArrayAsync(url, token);
        await File.WriteAllBytesAsync(filePath, data, token);
        reportProgress?.Invoke(100.0);
    }

    public void AddActionKeyword(string pluginId, string newActionKeyword) => _addActionKeywordAction?.Invoke(pluginId, newActionKeyword);
    public void RemoveActionKeyword(string pluginId, string oldActionKeyword) => _removeActionKeywordAction?.Invoke(pluginId, oldActionKeyword);
    public bool ActionKeywordAssigned(string actionKeyword) => _actionKeywordAssignedFunc?.Invoke(actionKeyword) ?? false;

    public void LogDebug(string className, string message, [CallerMemberName] string methodName = "") => Debug.WriteLine($"[DEBUG][{className}.{methodName}] {message}");
    public void LogInfo(string className, string message, [CallerMemberName] string methodName = "") => Debug.WriteLine($"[INFO][{className}.{methodName}] {message}");
    public void LogWarn(string className, string message, [CallerMemberName] string methodName = "") => Debug.WriteLine($"[WARN][{className}.{methodName}] {message}");
    public void LogError(string className, string message, [CallerMemberName] string methodName = "") => Debug.WriteLine($"[ERROR][{className}.{methodName}] {message}");
    public void LogException(string className, string message, Exception e, [CallerMemberName] string methodName = "") => Debug.WriteLine($"[EXCEPTION][{className}.{methodName}] {message}: {e}");

    public ValueTask<ImageSource?> LoadImageAsync(string path, bool loadFullImage = false, bool cacheImage = true) => ValueTask.FromResult<ImageSource?>(null);
    public Task<bool> UpdatePluginManifestAsync(bool usePrimaryUrlOnly = false, CancellationToken token = default) => Task.FromResult(false);
    public IReadOnlyList<UserPlugin> GetPluginManifest() => [];
    public bool PluginModified(string id) => false;
    public Task<bool> UpdatePluginAsync(PluginMetadata pluginMetadata, UserPlugin plugin, string zipFilePath) => Task.FromResult(false);
    public bool InstallPlugin(UserPlugin plugin, string zipFilePath) => false;
    public Task<bool> UninstallPluginAsync(PluginMetadata pluginMetadata, bool removePluginSettings = false) => Task.FromResult(false);

    public long StopwatchLogDebug(string className, string message, Action action, [CallerMemberName] string methodName = "")
    {
        var sw = Stopwatch.StartNew();
        action();
        sw.Stop();
        return sw.ElapsedMilliseconds;
    }

    public async Task<long> StopwatchLogDebugAsync(string className, string message, Func<Task> action, [CallerMemberName] string methodName = "")
    {
        var sw = Stopwatch.StartNew();
        await action();
        sw.Stop();
        return sw.ElapsedMilliseconds;
    }

    public long StopwatchLogInfo(string className, string message, Action action, [CallerMemberName] string methodName = "") => StopwatchLogDebug(className, message, action, methodName);
    public Task<long> StopwatchLogInfoAsync(string className, string message, Func<Task> action, [CallerMemberName] string methodName = "") => StopwatchLogDebugAsync(className, message, action, methodName);

    public bool IsApplicationDarkTheme() => PluginSdk.Services.ThemeService.IsDarkTheme;
    public string GetDataDirectory() => _storage.GetPluginSettingsDirectory(PluginSettingKey);
    public string GetLogDirectory() => Path.Combine(GetDataDirectory(), "Logs");
}
