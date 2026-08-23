using System.IO;
using System.Windows.Controls;
using Flow.Launcher.Plugin;
using Lertaro.Plugins.FlowLauncherBridge.Engine.SettingsTemplate;

namespace Lertaro.Plugins.FlowLauncherBridge.Engine.JsonRpc;

/// <summary>
/// Plugin adapter hosting external Flow.Launcher plugins (Python, Node.js, Executable).
/// Implements ISettingProvider dynamically when SettingsTemplate.yaml/json exists.
/// </summary>
public class FlowJsonRpcPlugin : IAsyncPlugin, ISettingProvider
{
    private readonly FlowProcessRunner _runner;
    private readonly PluginMetadata _metadata;
    private IPublicAPI? _api;

    public FlowJsonRpcPlugin(FlowProcessRunner runner, PluginMetadata metadata)
    {
        _runner = runner;
        _metadata = metadata;
    }

    public Task InitAsync(PluginInitContext context)
    {
        _api = context.API;
        return Task.CompletedTask;
    }

    public Task<List<Result>> QueryAsync(Query query, CancellationToken token)
    {
        if (_api == null)
            return Task.FromResult(new List<Result>());

        return _runner.ExecuteQueryAsync(query, _api, token);
    }

    public Control CreateSettingPanel()
    {
        if (string.IsNullOrEmpty(_metadata.PluginDirectory))
            return new UserControl();

        var candidates = new[] { "SettingsTemplate.yaml", "SettingsTemplate.yml", "SettingsTemplate.json" };
        string? templatePath = null;
        foreach (var name in candidates)
        {
            var p = Path.Combine(_metadata.PluginDirectory, name);
            if (File.Exists(p))
            {
                templatePath = p;
                break;
            }
        }

        if (templatePath == null)
            return new UserControl();

        var baseDir = PluginSdk.Services.UserDataService.GetUserDataDirectory()
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lertaro");

        var pluginName = !string.IsNullOrEmpty(_metadata.Name) ? _metadata.Name : _metadata.ID;
        var settingsPath = Path.Combine(baseDir, "FlowData", "Settings", "Plugins", pluginName, "Settings.json");

        return FlowSettingsTemplateBuilder.BuildSettingsPanel(templatePath, settingsPath);
    }
}
