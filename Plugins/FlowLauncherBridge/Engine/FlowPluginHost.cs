using System.Collections.Concurrent;
using System.IO;
using System.Reflection;
using System.Text.Json;
using Flow.Launcher.Plugin;
using Lertaro.Plugins.FlowLauncherBridge.Engine.SettingsTemplate;

namespace Lertaro.Plugins.FlowLauncherBridge.Engine;

/// <summary>
/// Discovers, loads, initializes, and unloads third-party Flow.Launcher plugins.
/// </summary>
public class FlowPluginHost : IAsyncDisposable
{
    private readonly List<string> _pluginDirectories = [];
    private readonly FlowSettingsStorage _storage;
    private readonly FlowPluginKeywordManager _keywordManager = new();
    private readonly ConcurrentDictionary<string, PluginPair> _loadedPlugins = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, FlowAssemblyLoader> _loaders = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, (PluginMetadata Metadata, string Reason)> _failedPlugins = new(StringComparer.OrdinalIgnoreCase);
    private readonly Action<string, bool>? _changeQueryAction;

    public FlowPluginHost(FlowSettingsStorage storage, IEnumerable<string>? pluginDirectories = null, Action<string, bool>? changeQueryAction = null)
    {
        _storage = storage;
        _changeQueryAction = changeQueryAction;

        if (pluginDirectories != null)
        {
            _pluginDirectories.AddRange(pluginDirectories);
        }
        else
        {
            var userDataDirectory = PluginSdk.Services.UserDataService.GetUserDataDirectory()
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lertaro");
            _pluginDirectories.Add(Path.Combine(userDataDirectory, "FlowData", "Plugins"));
        }
    }

    public IReadOnlyList<PluginPair> GlobalPlugins => _keywordManager.GlobalPlugins;
    public IReadOnlyDictionary<string, List<PluginPair>> KeywordPlugins => _keywordManager.KeywordPlugins;
    public IReadOnlyDictionary<string, (PluginMetadata Metadata, string Reason)> FailedPlugins => _failedPlugins;
    public List<PluginPair> GetAllPlugins() => _loadedPlugins.Values.ToList();

    public void RegisterPlugin(PluginPair pair)
    {
        _loadedPlugins[pair.Metadata.ID] = pair;
        _keywordManager.RegisterPluginKeywords(pair);
    }

    public bool OpenPluginSettings(string pluginId)
    {
        if (!_loadedPlugins.TryGetValue(pluginId, out _))
            return false;

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("lertaro://settings/page/Plugins") { UseShellExecute = true });
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void UpdatePluginActionKeyword(string pluginNameOrId, string newActionKeyword)
    {
        var pair = _loadedPlugins.Values.FirstOrDefault(p =>
            string.Equals(p.Metadata.ID, pluginNameOrId, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(p.Metadata.Name, pluginNameOrId, StringComparison.OrdinalIgnoreCase));
        if (pair != null)
            _keywordManager.UpdateActionKeyword(pair, newActionKeyword);
    }

    public void AddActionKeyword(string pluginId, string newActionKeyword)
    {
        if (_loadedPlugins.TryGetValue(pluginId, out var pair))
            _keywordManager.AddActionKeyword(pair, newActionKeyword);
    }

    public void RemoveActionKeyword(string pluginId, string oldActionKeyword)
    {
        if (_loadedPlugins.TryGetValue(pluginId, out var pair))
            _keywordManager.RemoveActionKeyword(pair, oldActionKeyword);
    }

    public bool ActionKeywordAssigned(string actionKeyword) => _keywordManager.ActionKeywordAssigned(actionKeyword);

    public async Task InitializeAsync()
    {
        foreach (var baseDir in _pluginDirectories)
        {
            if (!Directory.Exists(baseDir))
            {
                try { Directory.CreateDirectory(baseDir); } catch { continue; }
            }

            foreach (var pluginDir in Directory.GetDirectories(baseDir))
            {
                var manifestPath = Path.Combine(pluginDir, "plugin.json");
                if (!File.Exists(manifestPath))
                    continue;

                try
                {
                    await LoadPluginFromDirectoryAsync(pluginDir, manifestPath);
                }
                catch { }
            }
        }
    }

    public async Task<bool> LoadPluginFromDirectoryAsync(string pluginDir, string manifestPath)
    {
        var json = await File.ReadAllTextAsync(manifestPath);
        var metadata = JsonSerializer.Deserialize<PluginMetadata>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (metadata == null || metadata.Disabled || string.IsNullOrEmpty(metadata.ID))
            return false;

        metadata.PluginDirectory = pluginDir;
        var pName = !string.IsNullOrEmpty(metadata.Name) ? metadata.Name : metadata.ID;
        var baseDir = PluginSdk.Services.UserDataService.GetUserDataDirectory()
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lertaro");
        var sPath = Path.Combine(baseDir, "FlowData", "Settings", pName, "Settings.json");
        var customKeyword = FlowSettingsTemplateStorage.GetSettingValue(sPath, "triggerKeyword")?.ToString()
                         ?? FlowSettingsTemplateStorage.GetSettingValue(sPath, "ActionKeyword")?.ToString();
        if (!string.IsNullOrWhiteSpace(customKeyword))
            metadata.ActionKeyword = customKeyword;

        if (!string.IsNullOrWhiteSpace(metadata.ActionKeyword) && !metadata.ActionKeywords.Contains(metadata.ActionKeyword, StringComparer.OrdinalIgnoreCase))
        {
            metadata.ActionKeywords.Insert(0, metadata.ActionKeyword);
        }

        try
        {
            var pluginInstance = await FlowPluginLoaderHelper.CreatePluginInstanceAsync(
                metadata, pluginDir, _loaders, _failedPlugins).ConfigureAwait(false);

            if (pluginInstance != null)
            {
                var pair = new PluginPair { Metadata = metadata, Plugin = pluginInstance };
                _loadedPlugins[metadata.ID] = pair;
                _keywordManager.RegisterPluginKeywords(pair);

                var api = new FlowPublicApi(metadata, _storage, GetAllPlugins, _changeQueryAction, AddActionKeyword, RemoveActionKeyword, ActionKeywordAssigned);
                var initContext = new PluginInitContext(metadata, api);

                await pluginInstance.InitAsync(initContext);
                return true;
            }
            return false;
        }
        catch (BadImageFormatException ex)
        {
            _failedPlugins[metadata.ID] = (metadata, $"Architecture incompatible ({System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}): {ex.Message}");
            return false;
        }
        catch (ReflectionTypeLoadException ex)
        {
            var details = string.Join("; ", ex.LoaderExceptions.Where(e => e != null).Select(e => e!.Message));
            _failedPlugins[metadata.ID] = (metadata, $"Type load failed: {details}");
            return false;
        }
        catch (Exception ex)
        {
            _failedPlugins[metadata.ID] = (metadata, ex.Message);
            return false;
        }
    }

    public async Task<bool> UnloadPluginAsync(string pluginId)
    {
        if (string.IsNullOrEmpty(pluginId)) return false;

        if (_loadedPlugins.TryRemove(pluginId, out var pair))
        {
            _keywordManager.UnregisterPluginKeywords(pair);

            if (pair.Plugin is IAsyncDisposable asyncDisposable)
            {
                try { await asyncDisposable.DisposeAsync().ConfigureAwait(false); } catch { }
            }
            else if (pair.Plugin is IDisposable disposable)
            {
                try { disposable.Dispose(); } catch { }
            }
        }

        if (_loaders.TryRemove(pluginId, out var loader))
        {
            try { loader.Unload(); } catch { }
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        return true;
    }

    public void SaveAll()
    {
        _storage.SaveAll();

        foreach (var pair in _loadedPlugins.Values)
        {
            if (pair.Plugin is ISavable savable)
            {
                try { savable.Save(); } catch { }
            }
        }
    }

    public void RollbackAll() => _storage.ReloadAll();

    public async ValueTask DisposeAsync()
    {
        SaveAll();

        foreach (var pair in _loadedPlugins.Values)
        {
            if (pair.Plugin is IAsyncDisposable asyncDisposable)
            {
                try { await asyncDisposable.DisposeAsync().ConfigureAwait(false); } catch { }
            }
            else if (pair.Plugin is IDisposable disposable)
            {
                try { disposable.Dispose(); } catch { }
            }
        }

        foreach (var loader in _loaders.Values)
        {
            try { loader.Unload(); } catch { }
        }

        _loadedPlugins.Clear();
        _keywordManager.Clear();
        _loaders.Clear();
    }
}
