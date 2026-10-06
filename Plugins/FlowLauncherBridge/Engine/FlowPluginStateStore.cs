using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Lertaro.Plugins.FlowLauncherBridge.Engine;

/// <summary>
/// Persists and loads custom ActionKeyword and Disabled state overrides for Flow.Launcher plugins in FlowData\Settings\Plugins.json.
/// Kept isolated from plugin settings to prevent pollution.
/// </summary>
public static class FlowPluginStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string? CustomFilePath { get; set; }

    public static string GetFilePath()
    {
        if (!string.IsNullOrEmpty(CustomFilePath)) return CustomFilePath;
        var baseDir = PluginSdk.Services.UserDataService.GetUserDataDirectory()
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lertaro");
        return Path.Combine(baseDir, "FlowData", "Settings", "Plugins.json");
    }

    public static Dictionary<string, FlowPluginCustomState> LoadAll()
    {
        var result = new Dictionary<string, FlowPluginCustomState>(StringComparer.OrdinalIgnoreCase);
        var json = FlowSettingsStorage.ReadJsonFile(GetFilePath());
        if (json == null) return result;
        var root = JsonNode.Parse(json) as JsonObject ?? throw new InvalidDataException("Invalid Flow plugin enablement settings.");
        foreach (var (key, value) in root)
        {
            if (value is JsonValue legacy && legacy.TryGetValue<string>(out var keyword))
                result[key] = new FlowPluginCustomState { ActionKeyword = keyword };
            else if (value is JsonObject state)
                result[key] = state.Deserialize<FlowPluginCustomState>(JsonOptions)!;
            else
                throw new InvalidDataException($"Invalid state for Flow plugin '{key}'.");
        }
        return result;
    }
    public static string? GetCustomKeyword(string pluginName)
    {
        if (string.IsNullOrWhiteSpace(pluginName)) return null;
        var dict = LoadAll();
        return dict.TryGetValue(pluginName, out var state) && !string.IsNullOrWhiteSpace(state.ActionKeyword)
            ? state.ActionKeyword
            : null;
    }

    public static bool IsPluginDisabled(string pluginName)
    {
        if (string.IsNullOrWhiteSpace(pluginName)) return false;
        var dict = LoadAll();
        return dict.TryGetValue(pluginName, out var state) && state.Disabled;
    }

    public static void SaveCustomKeyword(string pluginName, string newKeyword)
    {
        if (string.IsNullOrWhiteSpace(pluginName)) return;
        var dict = LoadAll();
        if (!dict.TryGetValue(pluginName, out var state))
        {
            state = new FlowPluginCustomState();
            dict[pluginName] = state;
        }
        state.ActionKeyword = newKeyword;
        SaveAll(dict);
    }

    public static void SetPluginDisabled(string pluginName, bool disabled)
    {
        if (string.IsNullOrWhiteSpace(pluginName)) return;
        var dict = LoadAll();
        if (!dict.TryGetValue(pluginName, out var state))
        {
            state = new FlowPluginCustomState();
            dict[pluginName] = state;
        }
        state.Disabled = disabled;
        SaveAll(dict);
    }

    private static void SaveAll(Dictionary<string, FlowPluginCustomState> dict)
    {
        var path = GetFilePath();
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(dict, JsonOptions);
        FlowSettingsStorage.WriteJsonFile(path, json);
    }
}

public class FlowPluginCustomState
{
    public string? ActionKeyword { get; set; }
    public bool Disabled { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? AdditionalSettings { get; set; }
}
