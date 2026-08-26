using System.IO;
using Flow.Launcher.Plugin;

namespace Lertaro.Plugins.FlowLauncherBridge.Engine;

/// <summary>
/// Resolves the per-plugin data directories expected by Flow.Launcher plugins.
/// </summary>
internal static class FlowPluginPathHelper
{
    public static string GetSettingsDirectory(string userDataDirectory, string pluginName) =>
        Path.Combine(userDataDirectory, "FlowData", "Settings", "Plugins", pluginName);

    public static string GetCacheDirectory(string userDataDirectory, string pluginName) =>
        Path.Combine(userDataDirectory, "FlowData", "Caches", "Plugins", pluginName);

    public static void ApplyTo(PluginMetadata metadata)
    {
        var userDataDirectory = PluginSdk.Services.UserDataService.GetUserDataDirectory()
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lertaro");
        metadata.PluginSettingsDirectoryPath = GetSettingsDirectory(userDataDirectory, metadata.Name);
        metadata.PluginCacheDirectoryPath = GetCacheDirectory(userDataDirectory, metadata.Name);
        Directory.CreateDirectory(metadata.PluginSettingsDirectoryPath);
        Directory.CreateDirectory(metadata.PluginCacheDirectoryPath);
    }
}
