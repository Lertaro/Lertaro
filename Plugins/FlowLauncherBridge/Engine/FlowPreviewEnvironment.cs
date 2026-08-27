using System.IO;
using System.Windows.Controls;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.FlowLauncherBridge.Engine;

/// <summary>
/// Supplies the WebView2 user-data path while a third-party preview factory is materialized or its
/// WebView2 control is initialized. This is separate from the preview styler because some plugins
/// initialize WebView2 inside their control constructor, before the returned control can be styled.
/// </summary>
internal static class FlowPreviewEnvironment
{
    private const string WebView2UserDataFolder = "WEBVIEW2_USER_DATA_FOLDER";
    private static readonly object EnvironmentLock = new();
    private static int _scopeDepth;
    private static string? _previousValue;

    internal static IDisposable Enter()
    {
        var flowDataDir = GetFlowDataDirectory();
        try { Directory.CreateDirectory(flowDataDir); } catch { }

        lock (EnvironmentLock)
        {
            if (_scopeDepth == 0)
                _previousValue = Environment.GetEnvironmentVariable(WebView2UserDataFolder);

            _scopeDepth++;
            Environment.SetEnvironmentVariable(WebView2UserDataFolder, flowDataDir);
        }

        return new EnvironmentScope();
    }

    internal static UserControl CreatePreview(Lazy<UserControl> previewFactory)
    {
        using (Enter())
        {
            return previewFactory.Value;
        }
    }

    internal static string GetFlowDataDirectory()
    {
        var baseDir = UserDataService.GetUserDataDirectory()
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lertaro");
        return Path.Combine(baseDir, "FlowData");
    }

    private static void Exit()
    {
        lock (EnvironmentLock)
        {
            _scopeDepth--;
            if (_scopeDepth == 0)
            {
                Environment.SetEnvironmentVariable(WebView2UserDataFolder, _previousValue);
                _previousValue = null;
            }
        }
    }

    private sealed class EnvironmentScope : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                Exit();
        }
    }
}
