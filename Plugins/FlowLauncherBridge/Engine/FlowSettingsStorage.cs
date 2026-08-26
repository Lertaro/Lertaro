using System.IO;
using System.Text.Json;

namespace Lertaro.Plugins.FlowLauncherBridge.Engine;

/// <summary>
/// Manages per-user JSON settings storage for Flow.Launcher plugins.
/// Stores configuration files in the host-resolved user data directory under FlowData\Settings\Plugins\{pluginName}\.
/// to ensure complete multi-user session isolation.
/// </summary>
public class FlowSettingsStorage
{
    private readonly string _baseSettingsDirectory;
    private readonly Dictionary<string, object> _loadedSettings = [];
    private readonly object _lock = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public FlowSettingsStorage(string? baseSettingsDirectory = null)
    {
        if (!string.IsNullOrWhiteSpace(baseSettingsDirectory))
        {
            _baseSettingsDirectory = baseSettingsDirectory;
        }
        else
        {
            _baseSettingsDirectory = Path.Combine(
                PluginSdk.Services.UserDataService.GetUserDataDirectory() ?? AppDomain.CurrentDomain.BaseDirectory,
                "FlowData",
                "Settings",
                "Plugins");
        }

        if (!Directory.Exists(_baseSettingsDirectory))
        {
            Directory.CreateDirectory(_baseSettingsDirectory);
        }
    }

    public string GetPluginSettingsDirectory(string pluginId)
    {
        var dir = Path.Combine(_baseSettingsDirectory, pluginId);
        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
        return dir;
    }

    public T LoadSetting<T>(string pluginId) where T : new()
    {
        lock (_lock)
        {
            var cacheKey = $"{pluginId}_{typeof(T).FullName}";
            if (_loadedSettings.TryGetValue(cacheKey, out var existing) && existing is T cached)
            {
                return cached;
            }

            var filePath = Path.Combine(GetPluginSettingsDirectory(pluginId), $"{typeof(T).Name}.json");
            if (File.Exists(filePath))
            {
                try
                {
                    var json = File.ReadAllText(filePath);
                    var deserialized = JsonSerializer.Deserialize<T>(json, JsonOptions);
                    if (deserialized != null)
                    {
                        _loadedSettings[cacheKey] = deserialized;
                        return deserialized;
                    }
                }
                catch
                {
                    // Fall back to default instance if file is corrupt
                }
            }

            var newInstance = new T();
            _loadedSettings[cacheKey] = newInstance;
            return newInstance;
        }
    }

    public void SaveSetting<T>(string pluginId) where T : new()
    {
        // Defer disk write until SaveAll is called by the host upon clicking Save Settings
        lock (_lock)
        {
            var cacheKey = $"{pluginId}_{typeof(T).FullName}";
            if (!_loadedSettings.ContainsKey(cacheKey))
            {
                _loadedSettings[cacheKey] = new T();
            }
        }
    }

    public void SaveAll()
    {
        lock (_lock)
        {
            foreach (var (key, instance) in _loadedSettings)
            {
                var separatorIndex = key.IndexOf('_');
                if (separatorIndex <= 0)
                    continue;

                var pluginId = key[..separatorIndex];
                var typeName = instance.GetType().Name;
                var filePath = Path.Combine(GetPluginSettingsDirectory(pluginId), $"{typeName}.json");

                try
                {
                    var json = JsonSerializer.Serialize(instance, JsonOptions);
                    File.WriteAllText(filePath, json);
                }
                catch
                {
                    // Ignore transient write errors
                }
            }
        }
    }

    public void ReloadAll()
    {
        lock (_lock)
        {
            foreach (var (key, instance) in _loadedSettings)
            {
                var separatorIndex = key.IndexOf('_');
                if (separatorIndex <= 0) continue;

                var pluginId = key[..separatorIndex];
                var typeName = instance.GetType().Name;
                var filePath = Path.Combine(GetPluginSettingsDirectory(pluginId), $"{typeName}.json");

                if (File.Exists(filePath))
                {
                    try
                    {
                        var json = File.ReadAllText(filePath);
                        var diskObj = JsonSerializer.Deserialize(json, instance.GetType(), JsonOptions);
                        if (diskObj != null)
                        {
                            foreach (var prop in instance.GetType().GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
                            {
                                if (prop.CanRead && prop.CanWrite && prop.GetIndexParameters().Length == 0)
                                {
                                    var val = prop.GetValue(diskObj);
                                    prop.SetValue(instance, val);
                                }
                            }
                        }
                    }
                    catch { }
                }
            }
        }
    }

    public Dictionary<string, string> TakeSnapshot(string pluginId)
    {
        lock (_lock)
        {
            var snapshot = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (key, instance) in _loadedSettings)
            {
                if (key.StartsWith($"{pluginId}_", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        snapshot[key] = JsonSerializer.Serialize(instance, instance.GetType(), JsonOptions);
                    }
                    catch { }
                }
            }
            return snapshot;
        }
    }

    public void RestoreSnapshot(string pluginId, Dictionary<string, string> snapshot)
    {
        lock (_lock)
        {
            foreach (var (key, json) in snapshot)
            {
                if (_loadedSettings.TryGetValue(key, out var instance) && instance != null)
                {
                    try
                    {
                        var restored = JsonSerializer.Deserialize(json, instance.GetType(), JsonOptions);
                        if (restored != null)
                        {
                            foreach (var prop in instance.GetType().GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
                            {
                                if (prop.CanRead && prop.CanWrite && prop.GetIndexParameters().Length == 0)
                                {
                                    var val = prop.GetValue(restored);
                                    prop.SetValue(instance, val);
                                }
                            }
                        }
                    }
                    catch { }
                }
            }
        }
    }
}
