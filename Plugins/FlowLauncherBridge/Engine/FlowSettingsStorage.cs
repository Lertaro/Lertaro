using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Lertaro.Plugins.FlowLauncherBridge.Engine;

/// <summary>Per-user Flow configuration, stored separately from the host settings JSON.</summary>
public class FlowSettingsStorage
{
    private readonly string _baseSettingsDirectory;
    private readonly Dictionary<(string PluginId, Type Type), object> _loadedSettings = [];
    private readonly object _lock = new();
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public FlowSettingsStorage(string? baseSettingsDirectory = null)
    {
        _baseSettingsDirectory = !string.IsNullOrWhiteSpace(baseSettingsDirectory) ? baseSettingsDirectory
            : Path.Combine(PluginSdk.Services.UserDataService.GetUserDataDirectory() ?? AppDomain.CurrentDomain.BaseDirectory,
                "FlowData", "Settings", "Plugins");
        Directory.CreateDirectory(_baseSettingsDirectory);
    }

    public string GetPluginSettingsDirectory(string pluginId)
    {
        var directory = Path.Combine(_baseSettingsDirectory, pluginId);
        Directory.CreateDirectory(directory);
        return directory;
    }

    public T LoadSetting<T>(string pluginId) where T : new()
    {
        lock (_lock)
        {
            var key = (pluginId, typeof(T));
            if (_loadedSettings.TryGetValue(key, out var cached)) return (T)cached;
            var path = Path.Combine(GetPluginSettingsDirectory(pluginId), $"{typeof(T).Name}.json");
            var json = ReadJsonFile(path);
            T value;
            try { value = json == null ? new T() : JsonSerializer.Deserialize<T>(json, JsonOptions) ?? throw new InvalidDataException($"Invalid settings: {path}"); }
            catch (JsonException ex) { throw new InvalidDataException($"Invalid settings: {path}", ex); }
            _loadedSettings[key] = value!;
            return value;
        }
    }

    public void SaveSetting<T>(string pluginId) where T : new()
    {
        // Defer to the configuration commit; never substitute defaults for unreadable persisted data.
        _ = LoadSetting<T>(pluginId);
    }

    public void SaveAll()
    {
        lock (_lock)
        {
            foreach (var (key, instance) in _loadedSettings)
            {
                var path = Path.Combine(GetPluginSettingsDirectory(key.PluginId), $"{key.Type.Name}.json");
                var updated = JsonSerializer.SerializeToNode(instance, key.Type, JsonOptions);
                // Preserve fields added by a different plugin version, while retaining the plugin's
                // native file names and format. Nested third-party model migrations remain its own.
                if (ReadJsonFile(path) is { } previous && JsonNode.Parse(previous) is JsonObject original && updated is JsonObject changes)
                {
                    foreach (var (name, value) in changes) original[name] = value?.DeepClone();
                    updated = original;
                }
                WriteJsonFile(path, updated?.ToJsonString(JsonOptions) ?? "null");
            }
        }
    }

    public void ReloadAll()
    {
        lock (_lock)
        {
            foreach (var (key, instance) in _loadedSettings)
            {
                var path = Path.Combine(GetPluginSettingsDirectory(key.PluginId), $"{key.Type.Name}.json");
                if (ReadJsonFile(path) is { } json) RestoreObject(instance, key.Type, json);
            }
        }
    }

    public Dictionary<string, string> TakeSnapshot(string pluginId)
    {
        lock (_lock)
            return _loadedSettings.Where(pair => pair.Key.PluginId.Equals(pluginId, StringComparison.OrdinalIgnoreCase))
                .ToDictionary(pair => $"{pair.Key.PluginId}_{pair.Key.Type.FullName}",
                    pair => JsonSerializer.Serialize(pair.Value, pair.Key.Type, JsonOptions), StringComparer.OrdinalIgnoreCase);
    }

    public void RestoreSnapshot(string pluginId, Dictionary<string, string> snapshot)
    {
        lock (_lock)
        {
            foreach (var (key, instance) in _loadedSettings)
                if (key.PluginId.Equals(pluginId, StringComparison.OrdinalIgnoreCase) &&
                    snapshot.TryGetValue($"{key.PluginId}_{key.Type.FullName}", out var json))
                    RestoreObject(instance, key.Type, json);
        }
    }

    private static void RestoreObject(object instance, Type type, string json)
    {
        var restored = JsonSerializer.Deserialize(json, type, JsonOptions) ?? throw new InvalidDataException("Invalid plugin settings snapshot.");
        foreach (var property in type.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
            if (property.CanRead && property.CanWrite && property.GetIndexParameters().Length == 0)
                property.SetValue(instance, property.GetValue(restored));
    }

    internal static string? ReadJsonFile(string path)
    {
        try { return File.ReadAllText(path); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }

    // Shared by the bridge's typed, template and enablement stores; no dependency on the host's Core.
    internal static void WriteJsonFile(string path, string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        var created = false;
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream))
            {
                created = true;
                writer.Write(json);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }
        finally
        {
            if (created)
                try { File.Delete(temporary); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}
