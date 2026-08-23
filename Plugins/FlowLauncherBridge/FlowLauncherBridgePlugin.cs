using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.Plugins.FlowLauncherBridge.Engine;

namespace Lertaro.Plugins.FlowLauncherBridge;

/// <summary>
/// Main plugin entry point for FlowLauncherBridge.
/// Bridges third-party Flow.Launcher plugins into Lertaro.
/// </summary>
public class FlowLauncherBridgePlugin : IPlugin
{
    private static readonly FlowSettingsStorage SharedStorage = new();
    private static readonly FlowPluginHost SharedHost = new(SharedStorage);
    private static readonly FlowQueryDispatcher SharedDispatcher = new(SharedHost);

    public static FlowSettingsStorage Storage => SharedStorage;
    public static FlowPluginHost Host => SharedHost;
    public static FlowQueryDispatcher Dispatcher => SharedDispatcher;

    public FlowLauncherBridgePlugin() => _ = SharedHost.InitializeAsync();

    public string Name => PluginSdk.Services.TranslationService.Get("FlowLauncherBridge_PluginName");
    public string Description => PluginSdk.Services.TranslationService.Get("FlowLauncherBridge_PluginDesc");
}
