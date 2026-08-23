using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Lertaro.Plugins.FlowLauncherBridge.Engine.SettingsTemplate;

/// <summary>
/// Helper managing JSON settings persistence and default values for SettingsTemplate panels.
/// Split out from FlowSettingsTemplateBuilder to stay under the line limit.
/// </summary>
public static class FlowSettingsTemplateStorage
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static JsonObject LoadSettings(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var text = File.ReadAllText(path);
                var node = JsonNode.Parse(text);
                if (node is JsonObject obj) return obj;
            }
        }
        catch { }
        return new JsonObject();
    }

    public static void SaveSettings(string path, JsonObject obj)
    {
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var json = obj.ToJsonString(JsonOptions);
            File.WriteAllText(path, json);
        }
        catch { }
    }

    public static void EnsureDefaultSettings(string templateFilePath, string settingsJsonPath)
    {
        try
        {
            if (!File.Exists(templateFilePath)) return;
            var doc = FlowSettingsTemplateParser.ParseFile(templateFilePath);
            var settings = LoadSettings(settingsJsonPath);
            var changed = false;

            foreach (var elem in doc.Elements)
            {
                if (string.IsNullOrEmpty(elem.Name) || string.IsNullOrEmpty(elem.DefaultValue))
                    continue;

                if (!settings.ContainsKey(elem.Name))
                {
                    var type = elem.Type.ToLowerInvariant();
                    if (type == "checkbox" && bool.TryParse(elem.DefaultValue, out var b))
                    {
                        settings[elem.Name] = b;
                        changed = true;
                    }
                    else
                    {
                        settings[elem.Name] = elem.DefaultValue;
                        changed = true;
                    }
                }
            }

            if (changed)
            {
                SaveSettings(settingsJsonPath, settings);
            }
        }
        catch { }
    }
}
