using System.IO;
using System.Text.Json;

namespace Lertaro.Plugins.FlowLauncherBridge.Engine;

/// <summary>
/// Persists and loads custom ActionKeyword overrides for Flow.Launcher plugins in FlowData\PluginKeywords.json.
/// Separated from FlowPluginHost to stay under the 300-line limit.
/// </summary>
public static class FlowPluginKeywordStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string GetFilePath()
    {
        var baseDir = PluginSdk.Services.UserDataService.GetUserDataDirectory()
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lertaro");
        return Path.Combine(baseDir, "FlowData", "PluginKeywords.json");
    }

    public static Dictionary<string, string> LoadAll()
    {
        try
        {
            var path = GetFilePath();
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                if (dict != null)
                    return new Dictionary<string, string>(dict, StringComparer.OrdinalIgnoreCase);
            }
        }
        catch { }
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    public static string? GetCustomKeyword(string pluginId, string? pluginName = null)
    {
        var dict = LoadAll();
        if (dict.TryGetValue(pluginId, out var kwId) && !string.IsNullOrWhiteSpace(kwId))
            return kwId;
        if (!string.IsNullOrEmpty(pluginName) && dict.TryGetValue(pluginName, out var kwName) && !string.IsNullOrWhiteSpace(kwName))
            return kwName;
        return null;
    }

    public static void SaveCustomKeyword(string pluginId, string? pluginName, string newKeyword)
    {
        try
        {
            var dict = LoadAll();
            dict[pluginId] = newKeyword;
            if (!string.IsNullOrEmpty(pluginName))
                dict[pluginName] = newKeyword;

            var path = GetFilePath();
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(dict, JsonOptions);
            File.WriteAllText(path, json);
        }
        catch { }
    }
}
