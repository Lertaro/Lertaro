using System.Text.Json;
using Flow.Launcher.Plugin;
using Flow.Launcher.Plugin.SharedModels;
using Lertaro.Plugins.FlowLauncherBridge.Engine.JsonRpc;

namespace Lertaro.Plugins.FlowLauncherBridge.Tests.Engine.JsonRpc;

[TestClass]
public sealed class FlowJsonRpcSessionTests
{
    [TestMethod]
    public void HandleFuzzySearch_WithValidParameters_InvokesApiFuzzySearch()
    {
        string? passedQuery = null;
        string? passedText = null;
        var fakeApi = new FakePublicApi
        {
            FuzzySearchFunc = (q, t) =>
            {
                passedQuery = q;
                passedText = t;
                return new MatchResult(true, SearchPrecisionScore.Regular, [0, 1], 100);
            }
        };

        using var doc = JsonDocument.Parse("{\"id\":1,\"method\":\"FuzzySearch\",\"params\":[\"inzoi\",\"inZOI\"]}");
        var matchResult = FlowJsonRpcSession.HandleFuzzySearch(doc.RootElement, fakeApi);

        Assert.AreEqual("inzoi", passedQuery);
        Assert.AreEqual("inZOI", passedText);
        Assert.IsTrue(matchResult.Success);
        Assert.AreEqual(100, matchResult.Score);
    }

    [TestMethod]
    public void HandleFuzzySearch_WithNullApi_ReturnsFalse()
    {
        using var doc = JsonDocument.Parse("{\"id\":1,\"method\":\"FuzzySearch\",\"params\":[\"query\",\"text\"]}");
        var matchResult = FlowJsonRpcSession.HandleFuzzySearch(doc.RootElement, null);

        Assert.IsFalse(matchResult.Success);
        Assert.AreEqual(SearchPrecisionScore.Regular, matchResult.SearchPrecision);
    }

    [TestMethod]
    public void HandleOtherRpcCall_CopyToClipboard_InvokesApi()
    {
        string? copied = null;
        var fakeApi = new FakePublicApi
        {
            CopyToClipboardAction = t => copied = t
        };

        using var doc = JsonDocument.Parse("{\"id\":2,\"method\":\"CopyToClipboard\",\"params\":[\"copied text\"]}");
        FlowJsonRpcSession.HandleOtherRpcCall("CopyToClipboard", doc.RootElement, fakeApi);

        Assert.AreEqual("copied text", copied);
    }

    [TestMethod]
    public void HandleOtherRpcCall_OpenUrl_InvokesApi()
    {
        string? opened = null;
        var fakeApi = new FakePublicApi
        {
            OpenUrlAction = u => opened = u
        };

        using var doc = JsonDocument.Parse("{\"id\":3,\"method\":\"Flow.Launcher.OpenUrl\",\"params\":[\"https://playnite.link\"]}");
        FlowJsonRpcSession.HandleOtherRpcCall("Flow.Launcher.OpenUrl", doc.RootElement, fakeApi);

        Assert.AreEqual("https://playnite.link", opened);
    }

    private sealed class FakePublicApi : IPublicAPI
    {
        public Func<string, string, MatchResult>? FuzzySearchFunc { get; set; }
        public Action<string>? CopyToClipboardAction { get; set; }
        public Action<string>? OpenUrlAction { get; set; }

        public event VisibilityChangedEventHandler? VisibilityChanged { add { } remove { } }
        public event ActualApplicationThemeChangedEventHandler? ActualApplicationThemeChanged { add { } remove { } }
        public event EventHandler? StringMatcherBehaviorChanged { add { } remove { } }

        public MatchResult FuzzySearch(string query, string stringToCompare) =>
            FuzzySearchFunc != null ? FuzzySearchFunc(query, stringToCompare) : new MatchResult(false, SearchPrecisionScore.Regular);

        public void CopyToClipboard(string text, bool directCopy = false, bool showDefaultNotification = true) =>
            CopyToClipboardAction?.Invoke(text);

        public void OpenUrl(string url, bool? inPrivate = null) => OpenUrlAction?.Invoke(url);
        public void OpenUrl(Uri url, bool? inPrivate = null) => OpenUrlAction?.Invoke(url.ToString());
        public void OpenWebUrl(string url, bool? inPrivate = null) => OpenUrlAction?.Invoke(url);
        public void OpenWebUrl(Uri url, bool? inPrivate = null) => OpenUrlAction?.Invoke(url.ToString());
        public void OpenAppUri(string appUri) => OpenUrlAction?.Invoke(appUri);
        public void OpenAppUri(Uri appUri) => OpenUrlAction?.Invoke(appUri.ToString());

        public void ChangeQuery(string query, bool requery = false) { }
        public void RestartApp() { }
        public void ShellRun(string cmd, string filename = "cmd.exe") { }
        public void SaveAppAllSettings() { }
        public void SavePluginSettings() { }
        public Task ReloadAllPluginData() => Task.CompletedTask;
        public void CheckForNewUpdate() { }
        public void ShowMsgError(string title, string subTitle = "") { }
        public void ShowMsgErrorWithButton(string title, string buttonText, Action buttonAction, string subTitle = "") { }
        public void ShowMainWindow() { }
        public void FocusQueryTextBox() { }
        public void HideMainWindow() { }
        public bool IsMainWindowVisible() => true;
        public void ToggleGameMode() { }
        public void SetGameMode(bool value) { }
        public bool IsGameModeOn() => false;
        public void RegisterGlobalKeyboardCallback(Func<int, int, SpecialKeyState, bool> callback) { }
        public void RemoveGlobalKeyboardCallback(Func<int, int, SpecialKeyState, bool> callback) { }
        public void ShowMsg(string title, string subTitle = "", string iconPath = "") { }
        public void ShowMsg(string title, string subTitle, string iconPath, bool useMainWindowAsOwner = true) { }
        public void ShowMsgWithButton(string title, string buttonText, Action buttonAction, string subTitle = "", string iconPath = "") { }
        public void ShowMsgWithButton(string title, string buttonText, Action buttonAction, string subTitle, string iconPath, bool useMainWindowAsOwner = true) { }
        public System.Windows.MessageBoxResult ShowMsgBox(string messageBoxText, string caption = "", System.Windows.MessageBoxButton button = System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage icon = System.Windows.MessageBoxImage.None, System.Windows.MessageBoxResult defaultResult = System.Windows.MessageBoxResult.OK) => System.Windows.MessageBoxResult.OK;
        public Task ShowProgressBoxAsync(string caption, Func<Action<double>, Task> reportProgressAsync, Action? cancelProgress = null) => Task.CompletedTask;
        public void StartLoadingBar() { }
        public void StopLoadingBar() { }
        public T LoadSettingJsonStorage<T>() where T : new() => new();
        public void SaveSettingJsonStorage<T>() where T : new() { }
        public void SavePluginCaches() { }
        public Task<T> LoadCacheBinaryStorageAsync<T>(string cacheName, string cacheDirectory, T defaultData) where T : new() => Task.FromResult(defaultData);
        public Task SaveCacheBinaryStorageAsync<T>(string cacheName, string cacheDirectory) where T : new() => Task.CompletedTask;
        public void OpenSettingDialog() { }
        public bool OpenPluginSettingsWindow(string pluginId) => true;
        public string GetTranslation(string key) => string.Empty;
        public List<PluginPair> GetAllPlugins() => [];
        public List<PluginPair> GetAllInitializedPlugins(bool includeFailed) => [];
        public void OpenDirectory(string DirectoryPath, string? FileNameOrFilePath = null) { }
        public List<ThemeData> GetAvailableThemes() => [];
        public ThemeData GetCurrentTheme() => new("Default", string.Empty);
        public bool SetCurrentTheme(ThemeData theme) => true;
        public Task<string> HttpGetStringAsync(string url, CancellationToken token = default) => Task.FromResult(string.Empty);
        public Task<Stream> HttpGetStreamAsync(string url, CancellationToken token = default) => Task.FromResult<Stream>(new MemoryStream());
        public Task HttpDownloadAsync(string url, string filePath, Action<double>? reportProgress = null, CancellationToken token = default) => Task.CompletedTask;
        public void AddActionKeyword(string pluginId, string newActionKeyword) { }
        public void RemoveActionKeyword(string pluginId, string oldActionKeyword) { }
        public bool ActionKeywordAssigned(string actionKeyword) => false;
        public void LogDebug(string className, string message, [System.Runtime.CompilerServices.CallerMemberName] string methodName = "") { }
        public void LogInfo(string className, string message, [System.Runtime.CompilerServices.CallerMemberName] string methodName = "") { }
        public void LogWarn(string className, string message, [System.Runtime.CompilerServices.CallerMemberName] string methodName = "") { }
        public void LogError(string className, string message, [System.Runtime.CompilerServices.CallerMemberName] string methodName = "") { }
        public void LogException(string className, string message, Exception e, [System.Runtime.CompilerServices.CallerMemberName] string methodName = "") { }
        public ValueTask<System.Windows.Media.ImageSource?> LoadImageAsync(string path, bool loadFullImage = false, bool cacheImage = true) => ValueTask.FromResult<System.Windows.Media.ImageSource?>(null);
        public Task<bool> UpdatePluginManifestAsync(bool usePrimaryUrlOnly = false, CancellationToken token = default) => Task.FromResult(false);
        public IReadOnlyList<UserPlugin> GetPluginManifest() => [];
        public bool PluginModified(string id) => false;
        public Task<bool> UpdatePluginAsync(PluginMetadata pluginMetadata, UserPlugin plugin, string zipFilePath) => Task.FromResult(false);
        public bool InstallPlugin(UserPlugin plugin, string zipFilePath) => false;
        public Task<bool> UninstallPluginAsync(PluginMetadata pluginMetadata, bool removePluginSettings = false) => Task.FromResult(false);
        public long StopwatchLogDebug(string className, string message, Action action, [System.Runtime.CompilerServices.CallerMemberName] string methodName = "") { action(); return 0; }
        public Task<long> StopwatchLogDebugAsync(string className, string message, Func<Task> action, [System.Runtime.CompilerServices.CallerMemberName] string methodName = "") => Task.FromResult(0L);
        public long StopwatchLogInfo(string className, string message, Action action, [System.Runtime.CompilerServices.CallerMemberName] string methodName = "") { action(); return 0; }
        public Task<long> StopwatchLogInfoAsync(string className, string message, Func<Task> action, [System.Runtime.CompilerServices.CallerMemberName] string methodName = "") => Task.FromResult(0L);
        public bool IsApplicationDarkTheme() => false;
        public string GetDataDirectory() => string.Empty;
        public string GetLogDirectory() => string.Empty;
        public void ReQuery(bool reselect = true) { }
        public void BackToQueryResults() { }
    }
}
