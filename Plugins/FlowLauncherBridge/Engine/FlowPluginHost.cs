using System.Collections.Concurrent;
using System.IO;
using System.Reflection;
using System.Text.Json;
using Flow.Launcher.Plugin;

namespace Lertaro.Plugins.FlowLauncherBridge.Engine;

/// <summary>
/// Discovers, loads, and initializes third-party Flow.Launcher plugins.
/// </summary>
public class FlowPluginHost : IAsyncDisposable
{
    private readonly List<string> _pluginDirectories = [];
    private readonly FlowSettingsStorage _storage;
    private readonly ConcurrentDictionary<string, PluginPair> _loadedPlugins = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, List<PluginPair>> _keywordPlugins = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<PluginPair> _globalPlugins = [];
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
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var roamingAppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            _pluginDirectories.Add(Path.Combine(localAppData, "Lertaro", "FlowPlugins"));
            _pluginDirectories.Add(Path.Combine(roamingAppData, "Lertaro", "FlowPlugins"));
            _pluginDirectories.Add(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "FlowPlugins"));
        }
    }

    public IReadOnlyList<PluginPair> GlobalPlugins => _globalPlugins;
    public IReadOnlyDictionary<string, List<PluginPair>> KeywordPlugins => _keywordPlugins;
    public List<PluginPair> GetAllPlugins() => _loadedPlugins.Values.ToList();

    public bool OpenPluginSettings(string pluginId)
    {
        if (!_loadedPlugins.TryGetValue(pluginId, out var pair))
            return false;

        return FlowPluginSettingsHostWindow.ShowOrActivate(pair, _storage);
    }

    public void AddActionKeyword(string pluginId, string newActionKeyword)
    {
        if (string.IsNullOrWhiteSpace(newActionKeyword))
            return;

        if (!_loadedPlugins.TryGetValue(pluginId, out var pair))
            return;

        if (!pair.Metadata.ActionKeywords.Contains(newActionKeyword, StringComparer.OrdinalIgnoreCase))
        {
            pair.Metadata.ActionKeywords.Add(newActionKeyword);
        }

        _keywordPlugins.AddOrUpdate(
            newActionKeyword,
            _ => [pair],
            (_, list) => { lock (list) { if (!list.Contains(pair)) list.Add(pair); } return list; });
    }

    public void RemoveActionKeyword(string pluginId, string oldActionKeyword)
    {
        if (string.IsNullOrWhiteSpace(oldActionKeyword))
            return;

        if (_loadedPlugins.TryGetValue(pluginId, out var pair))
        {
            pair.Metadata.ActionKeywords.RemoveAll(k => string.Equals(k, oldActionKeyword, StringComparison.OrdinalIgnoreCase));
        }

        if (_keywordPlugins.TryGetValue(oldActionKeyword, out var list))
        {
            lock (list)
            {
                list.RemoveAll(p => string.Equals(p.Metadata.ID, pluginId, StringComparison.OrdinalIgnoreCase));
            }
        }
    }

    public bool ActionKeywordAssigned(string actionKeyword) => !string.IsNullOrWhiteSpace(actionKeyword) && _keywordPlugins.ContainsKey(actionKeyword);

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
                catch
                {
                    // Ignore corrupted or incompatible individual plugins
                }
            }
        }
    }

    private async Task LoadPluginFromDirectoryAsync(string pluginDir, string manifestPath)
    {
        var json = await File.ReadAllTextAsync(manifestPath);
        var metadata = JsonSerializer.Deserialize<PluginMetadata>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (metadata == null || metadata.Disabled || string.IsNullOrEmpty(metadata.ID))
            return;

        metadata.PluginDirectory = pluginDir;

        if (AllowedLanguage.IsDotNet(metadata.Language) && !string.IsNullOrEmpty(metadata.ExecuteFilePath) && File.Exists(metadata.ExecuteFilePath))
        {
            var loader = new FlowAssemblyLoader(pluginDir);
            var assembly = loader.LoadFromAssemblyPath(metadata.ExecuteFilePath);
            var pluginInstance = CreatePluginInstance(assembly);
            if (pluginInstance == null)
                return;

            var pair = new PluginPair { Metadata = metadata, Plugin = pluginInstance };
            _loadedPlugins[metadata.ID] = pair;
            RegisterPluginKeywords(pair);

            var api = new FlowPublicApi(metadata, _storage, GetAllPlugins, _changeQueryAction, AddActionKeyword, RemoveActionKeyword, ActionKeywordAssigned);
            var initContext = new PluginInitContext(metadata, api);

            await pluginInstance.InitAsync(initContext);
        }
    }

    private static IAsyncPlugin? CreatePluginInstance(Assembly assembly)
    {
        foreach (var type in assembly.GetTypes())
        {
            if (type.IsInterface || type.IsAbstract)
                continue;

            if (typeof(IAsyncPlugin).IsAssignableFrom(type))
            {
                return Activator.CreateInstance(type) as IAsyncPlugin;
            }

            if (typeof(IPlugin).IsAssignableFrom(type))
            {
                if (Activator.CreateInstance(type) is IPlugin syncPlugin)
                {
                    return new FlowSyncPluginAdapter(syncPlugin);
                }
            }
        }
        return null;
    }

    private void RegisterPluginKeywords(PluginPair pair)
    {
        var keywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(pair.Metadata.ActionKeyword))
            keywords.Add(pair.Metadata.ActionKeyword);
        if (pair.Metadata.ActionKeywords != null)
        {
            foreach (var kw in pair.Metadata.ActionKeywords)
                if (!string.IsNullOrWhiteSpace(kw))
                    keywords.Add(kw);
        }

        if (keywords.Contains("*") || keywords.Count == 0)
        {
            _globalPlugins.Add(pair);
        }

        foreach (var kw in keywords)
        {
            if (kw == "*")
                continue;

            _keywordPlugins.AddOrUpdate(
                kw,
                _ => [pair],
                (_, list) => { lock (list) { if (!list.Contains(pair)) list.Add(pair); } return list; });
        }
    }

    public async ValueTask DisposeAsync()
    {
        _storage.SaveAll();

        foreach (var pair in _loadedPlugins.Values)
        {
            if (pair.Plugin is ISavable savable)
            {
                try { savable.Save(); } catch { }
            }

            if (pair.Plugin is IAsyncDisposable asyncDisposable)
            {
                try { await asyncDisposable.DisposeAsync(); } catch { }
            }
            else if (pair.Plugin is IDisposable disposable)
            {
                try { disposable.Dispose(); } catch { }
            }
        }

        _loadedPlugins.Clear();
        _globalPlugins.Clear();
        _keywordPlugins.Clear();
    }
}
