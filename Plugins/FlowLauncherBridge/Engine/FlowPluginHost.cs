using System.Collections.Concurrent;
using System.IO;
using System.Reflection;
using System.Text.Json;
using Flow.Launcher.Plugin;

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
    private readonly ConcurrentDictionary<string, FlowPublicApi> _pluginApis = new(StringComparer.OrdinalIgnoreCase);
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
        if (!_loadedPlugins.TryGetValue(pluginId, out _)) return false;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("lertaro://settings/page/Plugins") { UseShellExecute = true });
            return true;
        }
        catch { return false; }
    }

    private PluginPair? FindPluginPair(string nameOrId) => _loadedPlugins.Values.FirstOrDefault(p =>
        string.Equals(p.Metadata.ID, nameOrId, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(p.Metadata.Name, nameOrId, StringComparison.OrdinalIgnoreCase));

    public bool IsPluginEnabled(string pluginNameOrId)
    {
        var pair = FindPluginPair(pluginNameOrId);
        return pair != null ? !pair.Metadata.Disabled : !FlowPluginStateStore.IsPluginDisabled(pluginNameOrId);
    }

    public void SetPluginEnabled(string pluginNameOrId, bool enabled)
    {
        var pair = FindPluginPair(pluginNameOrId);
        if (pair == null) return;
        pair.Metadata.Disabled = !enabled;
        FlowPluginStateStore.SetPluginDisabled(pair.Metadata.Name, !enabled);
        if (enabled) _keywordManager.RegisterPluginKeywords(pair);
        else _keywordManager.UnregisterPluginKeywords(pair);
    }

    public string GetPluginActionKeyword(string pluginNameOrId)
    {
        var pair = FindPluginPair(pluginNameOrId);
        if (pair != null && !string.IsNullOrEmpty(pair.Metadata.ActionKeyword)) return pair.Metadata.ActionKeyword;
        var targetName = pair?.Metadata.Name ?? pluginNameOrId;
        return FlowPluginStateStore.GetCustomKeyword(targetName) ?? pair?.Metadata.ActionKeyword ?? string.Empty;
    }

    public void UpdatePluginActionKeyword(string pluginNameOrId, string newActionKeyword)
    {
        if (string.IsNullOrWhiteSpace(newActionKeyword)) return;
        var pair = FindPluginPair(pluginNameOrId);
        if (pair == null) return;
        _keywordManager.UpdateActionKeyword(pair, newActionKeyword);
        FlowPluginStateStore.SaveCustomKeyword(pair.Metadata.Name, newActionKeyword);
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
                if (File.Exists(Path.Combine(pluginDir, ".deleted")))
                {
                    try { Directory.Delete(pluginDir, true); } catch { }
                }
            }

            foreach (var pluginDir in Directory.GetDirectories(baseDir))
            {
                var dirName = Path.GetFileName(pluginDir);
                var dashIndex = dirName.LastIndexOf('-');
                if (dashIndex > 0 && dashIndex == dirName.Length - 9)
                {
                    var standardDir = Path.Combine(baseDir, dirName[..dashIndex]);
                    if (!Directory.Exists(standardDir))
                    {
                        try { Directory.Move(pluginDir, standardDir); } catch { }
                    }
                }
            }

            foreach (var pluginDir in Directory.GetDirectories(baseDir))
            {
                var manifestPath = Path.Combine(pluginDir, "plugin.json");
                if (!File.Exists(manifestPath) || File.Exists(Path.Combine(pluginDir, ".deleted")))
                    continue;

                try { await LoadPluginFromDirectoryAsync(pluginDir, manifestPath); }
                catch { }
            }
        }
    }

    public async Task<bool> LoadPluginFromDirectoryAsync(string pluginDir, string manifestPath)
    {
        var json = await File.ReadAllTextAsync(manifestPath);
        var metadata = JsonSerializer.Deserialize<PluginMetadata>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (metadata == null || string.IsNullOrEmpty(metadata.ID))
            return false;

        metadata.PluginDirectory = pluginDir;
        FlowPluginPathHelper.ApplyTo(metadata);
        var customKeyword = FlowPluginStateStore.GetCustomKeyword(metadata.Name);
        var isDisabled = metadata.Disabled || FlowPluginStateStore.IsPluginDisabled(metadata.Name);
        metadata.Disabled = isDisabled;

        if (!string.IsNullOrWhiteSpace(customKeyword))
        {
            metadata.ActionKeyword = customKeyword;
            metadata.ActionKeywords.Clear();
            metadata.ActionKeywords.Add(customKeyword);
        }
        else if (!string.IsNullOrWhiteSpace(metadata.ActionKeyword) && !metadata.ActionKeywords.Contains(metadata.ActionKeyword, StringComparer.OrdinalIgnoreCase))
        {
            metadata.ActionKeywords.Insert(0, metadata.ActionKeyword);
        }

        try
        {
            var pluginInstance = await FlowPluginLoaderHelper.CreatePluginInstanceAsync(
                metadata, pluginDir, _loaders, _failedPlugins).ConfigureAwait(false);

            if (pluginInstance != null)
            {
                FlowPluginLanguageHelper.LoadPluginLanguage(pluginDir);
                var pair = new PluginPair { Metadata = metadata, Plugin = pluginInstance };
                _loadedPlugins[metadata.ID] = pair;
                if (!isDisabled)
                {
                    _keywordManager.RegisterPluginKeywords(pair);
                }

                var api = new FlowPublicApi(metadata, _storage, GetAllPlugins, _changeQueryAction, AddActionKeyword, RemoveActionKeyword, ActionKeywordAssigned);
                _pluginApis[metadata.ID] = api;
                var initContext = new PluginInitContext(metadata, api);

                await pluginInstance.InitAsync(initContext);

                if (pluginInstance is IPluginI18n pluginI18n)
                {
                    try { pluginI18n.OnCultureInfoChanged(FlowPluginLanguageHelper.GetEffectiveCulture()); } catch { }
                }

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

    public void NotifyVisibilityChanged(IEnumerable<PluginPair> targetPlugins, bool isVisible)
    {
        foreach (var pair in targetPlugins)
            if (_pluginApis.TryGetValue(pair.Metadata.ID, out var api)) api.RaiseVisibilityChanged(isVisible);
    }

    public void NotifyVisibilityChanged(bool isVisible)
    {
        foreach (var api in _pluginApis.Values) api.RaiseVisibilityChanged(isVisible);
    }

    public async Task<bool> UnloadPluginAsync(string pluginId)
    {
        if (string.IsNullOrEmpty(pluginId)) return false;

        _pluginApis.TryRemove(pluginId, out _);
        if (_loadedPlugins.TryRemove(pluginId, out var pair))
        {
            _keywordManager.UnregisterPluginKeywords(pair);
            if (pair.Plugin is IAsyncDisposable asyncDisposable)
                try { await asyncDisposable.DisposeAsync().ConfigureAwait(false); } catch { }
            else if (pair.Plugin is IDisposable disposable)
                try { disposable.Dispose(); } catch { }
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
            if (pair.Plugin is ISavable savable) try { savable.Save(); } catch { }
    }

    public void RollbackAll() => _storage.ReloadAll();

    public void UpdateCulture(string cultureName) => FlowPluginLanguageHelper.UpdatePluginsCulture(_loadedPlugins.Values, cultureName);

    public async ValueTask DisposeAsync()
    {
        SaveAll();
        foreach (var pair in _loadedPlugins.Values)
        {
            if (pair.Plugin is IAsyncDisposable asyncDisposable)
                try { await asyncDisposable.DisposeAsync().ConfigureAwait(false); } catch { }
            else if (pair.Plugin is IDisposable disposable)
                try { disposable.Dispose(); } catch { }
        }

        foreach (var loader in _loaders.Values) try { loader.Unload(); } catch { }

        _loadedPlugins.Clear();
        _pluginApis.Clear();
        _keywordManager.Clear();
        _loaders.Clear();
    }
}
