using System.Text.Json;

namespace Lertaro.Core;

internal static class PluginSettingsStore
{
    internal const string FileName = "plugin-settings.json";
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    internal static string PathFor(string userSettingsPath) => Path.Combine(Path.GetDirectoryName(userSettingsPath)!, FileName);

    internal static Dictionary<string, Dictionary<string, object>> Parse(string json)
    {
        var values = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, object>>>(json)
            ?? throw new InvalidDataException("Plugin settings must be an object.");
        return values.ToDictionary(p => p.Key,
            p => new Dictionary<string, object>(p.Value ?? throw new InvalidDataException("Plugin parameters must be an object."), StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);
    }

    internal static Dictionary<string, Dictionary<string, object>> Load(string userSettingsPath)
    {
        var path = PathFor(userSettingsPath);
        for (var index = 0; index <= 5; index++)
        {
            var json = SettingsFileReader.ReadIfPresent(index == 0 ? path : $"{path}.bak.{index}");
            if (json == null) continue;
            try { return Parse(json); }
            catch (Exception ex) when (ex is JsonException or InvalidDataException or ArgumentException) { }
        }
        throw new InvalidDataException($"No valid plugin settings remain at '{path}'. Restore a backup before saving.");
    }

    internal static void Save(string userSettingsPath, Dictionary<string, Dictionary<string, object>> values)
    {
        var path = PathFor(userSettingsPath);
        var json = JsonSerializer.Serialize(values, Options);
        _ = Parse(json);
        if (SettingsFileReader.ReadIfPresent(path) == json) return;
        UserSettingsBackupStore.Rotate(path, 5);
        AtomicFileStore.Write(path, json);
    }
}
