using System.Text.Json;
using Flow.Launcher.Plugin;
using Flow.Launcher.Plugin.SharedModels;
using Lertaro.Plugins.FlowLauncherBridge.Engine.JsonRpc;

namespace Lertaro.Plugins.FlowLauncherBridge.Tests.Engine.JsonRpc;

[TestClass]
public sealed class FlowProcessRunnerTests
{
    [TestMethod]
    public void JsonRpcResponse_Deserialization_Works()
    {
        const string json = "{\"result\":[{\"Title\":\"Hello\",\"SubTitle\":\"World\",\"IcoPath\":\"Images/app.png\",\"JsonRPCAction\":{\"method\":\"flow_open_url\",\"parameters\":[\"https://google.com\"]}}]}";
        var response = JsonSerializer.Deserialize<JsonRpcResponse>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.IsNotNull(response);
        Assert.IsNotNull(response.Result);
        Assert.HasCount(1, response.Result);
        Assert.AreEqual("Hello", response.Result[0].Title);
        Assert.AreEqual("World", response.Result[0].SubTitle);
        var action = response.Result[0].JsonRPCAction;
        Assert.IsNotNull(action);
        Assert.AreEqual("flow_open_url", action.Method);
    }

    [TestMethod]
    public void FlowJsonRpcPlugin_Constructs_AndInitializes()
    {
        var meta = new PluginMetadata { ID = "TEST_RPC", Name = "RpcPlugin" };
        var runner = new FlowProcessRunner(meta, "non_existent_binary.exe");
        var plugin = new FlowJsonRpcPlugin(runner, meta);
        Assert.IsNotNull(plugin);
    }

    [TestMethod]
    public async Task FlowProcessRunner_ExecuteActionAsync_ChangeQuery_InvokesApi()
    {
        var meta = new PluginMetadata { ID = "TEST_RPC", Name = "RpcPlugin" };
        var runner = new FlowProcessRunner(meta, "non_existent_binary.exe");

        string? capturedQuery = null;
        bool? capturedRequery = null;
        var fakeApi = new FakePublicApi((q, r) =>
        {
            capturedQuery = q;
            capturedRequery = r;
        });

        var action = new JsonRpcActionModel
        {
            Method = "Flow.Launcher.ChangeQuery",
            Parameters = ["tra en>zh ", true],
            DontHideAfterAction = true
        };

        await runner.ExecuteActionAsync(action, fakeApi);

        Assert.AreEqual("tra en>zh ", capturedQuery);
        Assert.IsTrue(capturedRequery);
    }

    private sealed class FakePublicApi : IPublicAPI
    {
        private readonly Action<string, bool> _changeQuery;
        public FakePublicApi(Action<string, bool> changeQuery) => _changeQuery = changeQuery;

        public event VisibilityChangedEventHandler? VisibilityChanged { add { } remove { } }
        public event ActualApplicationThemeChangedEventHandler? ActualApplicationThemeChanged { add { } remove { } }
        public event EventHandler? StringMatcherBehaviorChanged { add { } remove { } }

        public void ChangeQuery(string query, bool requery = false) => _changeQuery(query, requery);
        public void RestartApp() { }
        public void ShellRun(string cmd, string filename = "cmd.exe") { }
        public void CopyToClipboard(string text, bool directCopy = false, bool showDefaultNotification = true) { }
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
        public void OpenWebUrl(Uri url, bool? inPrivate = null) { }
        public void OpenWebUrl(string url, bool? inPrivate = null) { }
        public void OpenUrl(Uri url, bool? inPrivate = null) { }
        public void OpenUrl(string url, bool? inPrivate = null) { }
        public void OpenAppUri(Uri appUri) { }
        public void OpenAppUri(string appUri) { }
        public List<ThemeData> GetAvailableThemes() => [];
        public ThemeData GetCurrentTheme() => new("Default", string.Empty);
        public bool SetCurrentTheme(ThemeData theme) => true;
        public MatchResult FuzzySearch(string query, string stringToCompare) => new(false, SearchPrecisionScore.Regular);
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
