using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.Plugins.FlowLauncherBridge.Engine;
using Lertaro.Plugins.FlowLauncherBridge.Engine.SettingsTemplate;

namespace Lertaro.Plugins.FlowLauncherBridge;

/// <summary>
/// Main plugin entry point for FlowLauncherBridge.
/// Bridges third-party Flow.Launcher plugins into Lertaro.
/// </summary>
public class FlowLauncherBridgePlugin : IPlugin, IConfigurable
{
    private static readonly FlowSettingsStorage SharedStorage = new();
    private static readonly FlowPluginHost SharedHost = new(SharedStorage);
    private static readonly FlowQueryDispatcher SharedDispatcher = new(SharedHost);

    public static FlowSettingsStorage Storage => SharedStorage;
    public static FlowPluginHost Host => SharedHost;
    public static FlowQueryDispatcher Dispatcher => SharedDispatcher;

    public FlowLauncherBridgePlugin()
    {
        _ = SharedHost.InitializeAsync();
        PluginSdk.Services.PluginSettingsService.SettingChangedWithValue += OnSettingChanged;
    }

    public string Name => PluginSdk.Services.TranslationService.Get("FlowLauncherBridge_PluginName");
    public string Description => PluginSdk.Services.TranslationService.Get("FlowLauncherBridge_PluginDesc");

    public PluginConfigSchema GetConfigSchema() => FlowConfigSchemaBuilder.BuildSchema(SharedHost);

    private static void OnSettingChanged(string pluginId, string key, object? val)
    {
        if (!pluginId.Contains("FlowLauncherBridge", StringComparison.OrdinalIgnoreCase)) return;

        var dotIndex = key.IndexOf('.');
        if (dotIndex > 0)
        {
            var pluginName = key[..dotIndex];
            var settingKey = key[(dotIndex + 1)..];
            var baseDir = PluginSdk.Services.UserDataService.GetUserDataDirectory()
                ?? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lertaro");
            var settingsPath = FlowSettingsTemplateStorage.GetSettingsPath(baseDir, pluginName);
            FlowSettingsTemplateStorage.SaveSettingValue(settingsPath, settingKey, val);
        }

        SharedHost.SaveAll();
    }
}
