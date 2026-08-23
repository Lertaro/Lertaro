using System.IO;
using Flow.Launcher.Plugin;
using Lertaro.Plugins.FlowLauncherBridge.Engine.SettingsTemplate;

namespace Lertaro.Plugins.FlowLauncherBridge.Engine.JsonRpc;

/// <summary>
/// Plugin adapter hosting external Flow.Launcher plugins (Python, Node.js, Executable).
/// Implements ISettingProvider dynamically when SettingsTemplate.yaml/json exists.
/// </summary>
public class FlowJsonRpcPlugin : IAsyncPlugin
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
        TryEnsureDefaultSettings();
        return Task.CompletedTask;
    }

    public Task<List<Result>> QueryAsync(Query query, CancellationToken token)
    {
        if (_api == null)
            return Task.FromResult(new List<Result>());

        return _runner.ExecuteQueryAsync(query, _api, token);
    }

    public static bool HasSettingsTemplate(string? pluginDirectory) => GetSettingsTemplatePath(pluginDirectory) != null;

    public static string? GetSettingsTemplatePath(string? pluginDirectory)
    {
        if (string.IsNullOrEmpty(pluginDirectory)) return null;
        var candidates = new[] { "SettingsTemplate.yaml", "SettingsTemplate.yml", "SettingsTemplate.json" };
        foreach (var name in candidates)
        {
            var p = Path.Combine(pluginDirectory, name);
            if (File.Exists(p)) return p;
        }
        return null;
    }

    private void TryEnsureDefaultSettings()
    {
        var templatePath = GetSettingsTemplatePath(_metadata.PluginDirectory);
        if (templatePath != null)
        {
            var baseDir = PluginSdk.Services.UserDataService.GetUserDataDirectory()
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lertaro");
            var pluginName = !string.IsNullOrEmpty(_metadata.Name) ? _metadata.Name : _metadata.ID;
            var settingsPath = FlowSettingsTemplateStorage.GetSettingsPath(baseDir, pluginName);
            FlowSettingsTemplateStorage.EnsureDefaultSettings(templatePath, settingsPath);
        }
    }
}
