using System.Collections.Concurrent;
using System.IO;
using System.Reflection;
using System.Text.Json;
using Flow.Launcher.Plugin;
using Lertaro.Plugins.FlowLauncherBridge.Engine.JsonRpc;

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

    public IReadOnlyList<PluginPair> GlobalPlugins => _globalPlugins;
    public IReadOnlyDictionary<string, List<PluginPair>> KeywordPlugins => _keywordPlugins;
    public IReadOnlyDictionary<string, (PluginMetadata Metadata, string Reason)> FailedPlugins => _failedPlugins;
    public List<PluginPair> GetAllPlugins() => _loadedPlugins.Values.ToList();
    public void RegisterPlugin(PluginPair pair) => _loadedPlugins[pair.Metadata.ID] = pair;

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

    public void AddActionKeyword(string pluginId, string newActionKeyword)
    {
        if (string.IsNullOrWhiteSpace(newActionKeyword) || !_loadedPlugins.TryGetValue(pluginId, out var pair)) return;
        if (!pair.Metadata.ActionKeywords.Contains(newActionKeyword, StringComparer.OrdinalIgnoreCase)) pair.Metadata.ActionKeywords.Add(newActionKeyword);
        _keywordPlugins.AddOrUpdate(newActionKeyword, _ => [pair], (_, list) => { lock (list) { if (!list.Contains(pair)) list.Add(pair); } return list; });
    }

    public void RemoveActionKeyword(string pluginId, string oldActionKeyword)
    {
        if (string.IsNullOrWhiteSpace(oldActionKeyword)) return;
        if (_loadedPlugins.TryGetValue(pluginId, out var pair)) pair.Metadata.ActionKeywords.RemoveAll(k => string.Equals(k, oldActionKeyword, StringComparison.OrdinalIgnoreCase));
        if (_keywordPlugins.TryGetValue(oldActionKeyword, out var list)) lock (list) { list.RemoveAll(p => string.Equals(p.Metadata.ID, pluginId, StringComparison.OrdinalIgnoreCase)); }
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
                catch { }
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
        if (!string.IsNullOrWhiteSpace(metadata.ActionKeyword) && !metadata.ActionKeywords.Contains(metadata.ActionKeyword, StringComparer.OrdinalIgnoreCase))
        {
            metadata.ActionKeywords.Insert(0, metadata.ActionKeyword);
        }

        try
        {
            IAsyncPlugin? pluginInstance = null;

            if (AllowedLanguage.IsDotNet(metadata.Language) && !string.IsNullOrEmpty(metadata.ExecuteFilePath) && File.Exists(metadata.ExecuteFilePath))
            {
                var loader = new FlowAssemblyLoader(pluginDir);
                var assembly = loader.LoadFromAssemblyPath(metadata.ExecuteFilePath);
                pluginInstance = CreatePluginInstance(assembly);
                if (pluginInstance == null)
                {
                    _failedPlugins[metadata.ID] = (metadata, "No implementation of IPlugin or IAsyncPlugin found.");
                    return;
                }
            }
            else if (AllowedLanguage.IsPython(metadata.Language))
            {
                var pythonPath = await FlowEnvironmentLocator.EnsurePythonExecutableAsync().ConfigureAwait(false);
                if (string.IsNullOrEmpty(pythonPath))
                {
                    _failedPlugins[metadata.ID] = (metadata, "Python runtime not found in PythonEmbeded, or download failed.");
                    return;
                }
                FlowPipManager.EnsurePipAndRequirementsBackground(pythonPath, pluginDir);
                var runner = new FlowProcessRunner(metadata, pythonPath, metadata.ExecuteFilePath);
                pluginInstance = new FlowJsonRpcPlugin(runner, metadata);
            }
            else if (AllowedLanguage.IsNodeJs(metadata.Language))
            {
                var nodePath = await FlowEnvironmentLocator.EnsureNodeExecutableAsync().ConfigureAwait(false);
                if (string.IsNullOrEmpty(nodePath) || !File.Exists(nodePath))
                {
                    _failedPlugins[metadata.ID] = (metadata, "Node.js runtime not found in NodeEmbeded, and download failed.");
                    return;
                }
                FlowNpmManager.EnsureNpmAndPackagesBackground(nodePath, pluginDir);
                var runner = new FlowProcessRunner(metadata, nodePath, metadata.ExecuteFilePath);
                pluginInstance = new FlowJsonRpcPlugin(runner, metadata);
            }
            else if (AllowedLanguage.IsExecutable(metadata.Language))
            {
                if (string.IsNullOrEmpty(metadata.ExecuteFilePath) || !File.Exists(metadata.ExecuteFilePath))
                {
                    _failedPlugins[metadata.ID] = (metadata, $"Executable file not found: {metadata.ExecuteFilePath}");
                    return;
                }
                var runner = new FlowProcessRunner(metadata, metadata.ExecuteFilePath);
                pluginInstance = new FlowJsonRpcPlugin(runner, metadata);
            }

            if (pluginInstance != null)
            {
                var pair = new PluginPair { Metadata = metadata, Plugin = pluginInstance };
                _loadedPlugins[metadata.ID] = pair;
                RegisterPluginKeywords(pair);

                var api = new FlowPublicApi(metadata, _storage, GetAllPlugins, _changeQueryAction, AddActionKeyword, RemoveActionKeyword, ActionKeywordAssigned);
                var initContext = new PluginInitContext(metadata, api);

                await pluginInstance.InitAsync(initContext);
            }
        }
        catch (BadImageFormatException ex)
        {
            _failedPlugins[metadata.ID] = (metadata, $"Architecture incompatible ({System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}): {ex.Message}");
        }
        catch (ReflectionTypeLoadException ex)
        {
            var details = string.Join("; ", ex.LoaderExceptions.Where(e => e != null).Select(e => e!.Message));
            _failedPlugins[metadata.ID] = (metadata, $"Type load failed: {details}");
        }
        catch (Exception ex)
        {
            _failedPlugins[metadata.ID] = (metadata, ex.Message);
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
