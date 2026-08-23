using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Lertaro.Plugins.FlowLauncherBridge.Engine;

/// <summary>
/// Persists and loads custom ActionKeyword and Disabled state overrides for Flow.Launcher plugins in FlowData\Plugins.json.
/// Kept isolated from plugin settings to prevent pollution.
/// </summary>
public static class FlowPluginStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string GetFilePath()
    {
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
                            if (stateObj.TryGetPropertyValue("ActionKeyword", out var kwNode) && kwNode != null)
                                state.ActionKeyword = kwNode.ToString();
                            if (stateObj.TryGetPropertyValue("Disabled", out var disNode) && disNode != null && bool.TryParse(disNode.ToString(), out var dis))
                                state.Disabled = dis;
                            result[key] = state;
                        }
                    }
                }
            }
        }
        catch { }
        return result;
    }

    public static string? GetCustomKeyword(string pluginId, string? pluginName = null)
    {
        var dict = LoadAll();
        if (dict.TryGetValue(pluginId, out var stateId) && !string.IsNullOrWhiteSpace(stateId.ActionKeyword))
            return stateId.ActionKeyword;
        if (!string.IsNullOrEmpty(pluginName) && dict.TryGetValue(pluginName, out var stateName) && !string.IsNullOrWhiteSpace(stateName.ActionKeyword))
            return stateName.ActionKeyword;
        return null;
    }

    public static bool IsPluginDisabled(string pluginId, string? pluginName = null)
    {
        var dict = LoadAll();
        if (dict.TryGetValue(pluginId, out var stateId))
            return stateId.Disabled;
        if (!string.IsNullOrEmpty(pluginName) && dict.TryGetValue(pluginName, out var stateName))
            return stateName.Disabled;
        return false;
    }

    public static void SaveCustomKeyword(string pluginId, string? pluginName, string newKeyword)
    {
        try
        {
            var dict = LoadAll();
            if (!dict.TryGetValue(pluginId, out var stateId))
            {
                stateId = new FlowPluginCustomState();
                dict[pluginId] = stateId;
            }
            stateId.ActionKeyword = newKeyword;

            if (!string.IsNullOrEmpty(pluginName))
            {
                if (!dict.TryGetValue(pluginName, out var stateName))
                {
                    stateName = new FlowPluginCustomState();
                    dict[pluginName] = stateName;
                }
                stateName.ActionKeyword = newKeyword;
            }

            SaveAll(dict);
        }
        catch { }
    }

    public static void SetPluginDisabled(string pluginId, string? pluginName, bool disabled)
    {
        try
        {
            var dict = LoadAll();
            if (!dict.TryGetValue(pluginId, out var stateId))
            {
                stateId = new FlowPluginCustomState();
                dict[pluginId] = stateId;
            }
            stateId.Disabled = disabled;

            if (!string.IsNullOrEmpty(pluginName))
            {
                if (!dict.TryGetValue(pluginName, out var stateName))
                {
                    stateName = new FlowPluginCustomState();
                    dict[pluginName] = stateName;
                }
                stateName.Disabled = disabled;
            }

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
