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

    public static string GetSettingsPath(string baseDir, string pluginName)
    {
        var primaryPath = Path.Combine(baseDir, "FlowData", "Settings", "Plugins", pluginName, "Settings.json");
        var legacyPath = Path.Combine(baseDir, "FlowData", "Settings", pluginName, "Settings.json");
        if (File.Exists(legacyPath) && !File.Exists(primaryPath))
        {
            try
            {
                var dir = Path.GetDirectoryName(primaryPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.Copy(legacyPath, primaryPath, true);
            }
            catch { }
        }
        return primaryPath;
    }

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

            // Also mirror to legacy path if in standard path
            if (path.Contains(Path.Combine("Settings", "Plugins")))
            {
                var legacyPath = path.Replace(Path.Combine("Settings", "Plugins"), "Settings");
                var legacyDir = Path.GetDirectoryName(legacyPath);
                if (!string.IsNullOrEmpty(legacyDir) && !Directory.Exists(legacyDir))
                    Directory.CreateDirectory(legacyDir);
                File.WriteAllText(legacyPath, json);
            }
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
                if (string.IsNullOrEmpty(elem.Name))
                    continue;

                if (!settings.ContainsKey(elem.Name))
                {
                    var defVal = elem.DefaultValue ?? string.Empty;
                    var type = elem.Type.ToLowerInvariant();
                    if (type == "checkbox" && bool.TryParse(defVal, out var b))
                    {
                        settings[elem.Name] = b;
                        changed = true;
                    }
                    else if ((type == "number" || type == "integer" || type == "numeric") && int.TryParse(defVal, out var num))
                    {
                        settings[elem.Name] = num;
                        changed = true;
                    }
                    else
                    {
                        settings[elem.Name] = defVal;
                        changed = true;
                    }
                }
                else if (settings[elem.Name] is JsonValue val && val.TryGetValue<string>(out var strVal) && strVal.Contains("\\n"))
                {
                    settings[elem.Name] = strVal.Replace("\\n", "\n").Replace("\\r", "\r");
                    changed = true;
                }
            }

            if (changed)
            {
                SaveSettings(settingsJsonPath, settings);
            }
        }
        catch { }
    }

    public static void SaveSettingValue(string path, string key, object? value)
    {
        try
        {
            var settings = LoadSettings(path);
            if (value == null)
            {
                settings.Remove(key);
            }
            else if (value is bool b)
            {
                settings[key] = b;
            }
            else if (value is int i)
            {
                settings[key] = i;
            }
            else if (value is System.Collections.IEnumerable en && !(value is string))
            {
                var items = new List<string>();
                foreach (var item in en)
                {
                    if (item != null) items.Add(item.ToString() ?? string.Empty);
                }
                settings[key] = string.Join("\n", items);
            }
            else if (value is JsonElement el)
            {
                if (el.ValueKind == JsonValueKind.True) settings[key] = true;
                else if (el.ValueKind == JsonValueKind.False) settings[key] = false;
                else if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var num)) settings[key] = num;
                else settings[key] = el.GetString();
            }
            else
            {
                settings[key] = value.ToString();
            }
            SaveSettings(path, settings);
        }
        catch { }
    }

    public static object? GetSettingValue(string path, string key)
    {
        try
        {
            var settings = LoadSettings(path);
            if (settings.TryGetPropertyValue(key, out var node) && node != null)
            {
                if (node.GetValueKind() == JsonValueKind.True) return true;
                if (node.GetValueKind() == JsonValueKind.False) return false;
                if (node.GetValueKind() == JsonValueKind.Number && int.TryParse(node.ToString(), out var num)) return num;
                return node.ToString();
            }
        }
        catch { }
        return null;
    }
}
