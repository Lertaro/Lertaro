using System.IO;
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
    private static readonly Lazy<FlowSettingsStorage> SharedStorage = new(() => new FlowSettingsStorage());
    private static readonly Lazy<FlowPluginHost> SharedHost = new(() => new FlowPluginHost(SharedStorage.Value));
    private static readonly Lazy<FlowQueryDispatcher> SharedDispatcher = new(() => new FlowQueryDispatcher(SharedHost.Value));
    private static readonly SemaphoreSlim HostLifecycleGate = new(1, 1);
    private static bool _hostInitialized;

    public static FlowSettingsStorage Storage => SharedStorage.Value;
    public static FlowPluginHost Host => SharedHost.Value;
    public static FlowQueryDispatcher Dispatcher => SharedDispatcher.Value;

    public FlowLauncherBridgePlugin()
    {
        PluginSdk.Services.PluginSettingsService.ComponentEnablementChanged += UpdateHostState;
        PluginSdk.Services.PluginSettingsService.SettingChangedWithValue += OnSettingChanged;
        PluginSdk.Services.TranslationService.CultureChanged += OnCultureChanged;
        UpdateHostState();
    }

    public string Name => PluginSdk.Services.TranslationService.Get("FlowLauncherBridge_PluginName");
    public string Description => PluginSdk.Services.TranslationService.Get("FlowLauncherBridge_PluginDesc");
    public string? WebsiteUrl => "https://www.flowlauncher.com/plugins";
    public string? WebsiteLabel => PluginSdk.Services.TranslationService.Get("FlowLauncherBridge_WebsiteLabel");

    public PluginConfigSchema GetConfigSchema() => FlowConfigSchemaBuilder.BuildSchema(Host);

    private static void UpdateHostState() => _ = ApplyHostStateAsync();

    private static async Task ApplyHostStateAsync()
    {
        await HostLifecycleGate.WaitAsync().ConfigureAwait(false);
        try
        {
            var dllName = Path.GetFileName(typeof(FlowLauncherBridgePlugin).Assembly.Location);
            var shouldRun = FlowLauncherBridgeEnablement.IsRuntimeEnabled(
                PluginSdk.Services.PluginSettingsService.IsComponentEnabled, dllName);

            if (shouldRun && !_hostInitialized)
            {
                _hostInitialized = true;
                await Host.InitializeAsync().ConfigureAwait(false);
            }
            else if (!shouldRun && _hostInitialized)
            {
                await Host.DisposeAsync().ConfigureAwait(false);
                _hostInitialized = false;
            }
        }
        catch (Exception ex)
        {
            PluginSdk.Logger.Log($"[FlowLauncherBridge] Failed to update host state: {ex.Message}", PluginSdk.LogLevel.Error);
        }
        finally
        {
            HostLifecycleGate.Release();
        }
    }

    private static void OnSettingChanged(string pluginId, string key, object? val)
    {
        if (!pluginId.Contains("FlowLauncherBridge", StringComparison.OrdinalIgnoreCase)) return;
        if (!IsRuntimeEnabled()) return;

        var dotIndex = key.IndexOf('.');
        if (dotIndex > 0)
        {
            var pluginName = key[..dotIndex];
            var settingKey = key[(dotIndex + 1)..];
            var baseDir = PluginSdk.Services.UserDataService.GetUserDataDirectory()
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lertaro");
            var settingsPath = FlowSettingsTemplateStorage.GetSettingsPath(baseDir, pluginName);
            FlowSettingsTemplateStorage.SaveSettingValue(settingsPath, settingKey, val);
        }

        Host.SaveAll();
    }

    private static bool IsRuntimeEnabled()
    {
        var dllName = Path.GetFileName(typeof(FlowLauncherBridgePlugin).Assembly.Location);
        return FlowLauncherBridgeEnablement.IsRuntimeEnabled(
            PluginSdk.Services.PluginSettingsService.IsComponentEnabled, dllName);
    }

    private static void OnCultureChanged(string cultureName)
    {
        if (SharedHost.IsValueCreated)
            SharedHost.Value.UpdateCulture(cultureName);
    }
}
