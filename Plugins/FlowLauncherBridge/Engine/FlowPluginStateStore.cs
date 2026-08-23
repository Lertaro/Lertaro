using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Lertaro.Plugins.FlowLauncherBridge.Engine;

/// <summary>
/// Persists and loads custom ActionKeyword and Disabled state overrides for Flow.Launcher plugins in FlowData\Plugins.json.
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
        try
        {
            var path = GetFilePath();
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var node = JsonNode.Parse(json);
                if (node is JsonObject obj)
                {
                    foreach (var (key, valNode) in obj)
                    {
                        if (valNode is JsonValue jVal && jVal.TryGetValue<string>(out var strVal))
                        {
                            result[key] = new FlowPluginCustomState { ActionKeyword = strVal };
                        }
                        else if (valNode is JsonObject stateObj)
                        {
                            var state = new FlowPluginCustomState();
                            if (stateObj.TryGetPropertyValue("ActionKeyword", out var kwNode) && kwNode is JsonValue kwVal && kwVal.TryGetValue<string>(out var kwStr))
                                state.ActionKeyword = kwStr;
                            if (stateObj.TryGetPropertyValue("Disabled", out var disNode) && disNode is JsonValue disVal && disVal.TryGetValue<bool>(out var disBool))
                                state.Disabled = disBool;
                            result[key] = state;
                        }
                    }
                }
            }
        }
        catch { }
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
        try
        {
            var dict = LoadAll();
            if (!dict.TryGetValue(pluginName, out var state))
            {
                state = new FlowPluginCustomState();
                dict[pluginName] = state;
            }
            state.ActionKeyword = newKeyword;
            SaveAll(dict);
        }
        catch { }
    }

    public static void SetPluginDisabled(string pluginName, bool disabled)
    {
        if (string.IsNullOrWhiteSpace(pluginName)) return;
        try
        {
            var dict = LoadAll();
            if (!dict.TryGetValue(pluginName, out var state))
            {
                state = new FlowPluginCustomState();
                dict[pluginName] = state;
            }
            state.Disabled = disabled;
            SaveAll(dict);
        }
        catch { }
    }

    private static void SaveAll(Dictionary<string, FlowPluginCustomState> dict)
    {
        var path = GetFilePath();
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(dict, JsonOptions);
        File.WriteAllText(path, json);
    }
}

public class FlowPluginCustomState
{
    public string? ActionKeyword { get; set; }
    public bool Disabled { get; set; }
}
