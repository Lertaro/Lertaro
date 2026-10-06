using System.Reflection;
using System.Text.Json;

namespace Lertaro.Core;

internal static class SettingsBackupInventory
{
    public static List<SettingsBackupPlugin> Read(SettingsBackupPaths source, string? applicationDirectory)
    {
        var plugins = new List<SettingsBackupPlugin>();
        using var settingsStream = source.Read("user-settings.json");
        using var settings = JsonDocument.Parse(SettingsBackupFormat.ReadJson(settingsStream));
        var disabledComponents = settings.RootElement.TryGetProperty("DisabledPluginComponents", out var components) && components.ValueKind == JsonValueKind.Array
            ? components.EnumerateArray().Where(c => c.ValueKind == JsonValueKind.String).Select(c => c.GetString()!).ToArray() : [];
        string[] Disabled(string key) => disabledComponents.Where(c => c.StartsWith(key + ".dll::", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (settings.RootElement.TryGetProperty("PluginSettings", out var native) && native.ValueKind == JsonValueKind.Object)
            foreach (var entry in native.EnumerateObject())
                plugins.Add(new(null, entry.Name, "native", null, null, null, [entry.Name], null, null, DisabledComponents: Disabled(entry.Name)));

        // This set is intentionally independent of loaded/enabled plugin instances.
        var states = new Dictionary<string, bool?>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var stream = source.Read("FlowData/Settings/Plugins.json");
            using var json = JsonDocument.Parse(SettingsBackupFormat.ReadJson(stream));
            foreach (var entry in json.RootElement.EnumerateObject())
                states[entry.Name] = entry.Value.ValueKind == JsonValueKind.Object && entry.Value.TryGetProperty("Disabled", out var disabled)
                    ? disabled.ValueKind == JsonValueKind.True : null;
        }
        catch (FileNotFoundException) { }
        foreach (var file in source.Files("FlowData/Settings/Plugins"))
        {
            var name = file.Split('/')[3];
            states.TryAdd(name, null);
        }
        foreach (var directory in source.Directories("FlowData/Plugins"))
        {
            var file = directory + "/plugin.json";
            FileStream stream;
            try { stream = source.Read(file); }
            catch (FileNotFoundException) { continue; }
            using var pinnedManifest = stream;
            using var json = JsonDocument.Parse(SettingsBackupFormat.ReadJson(stream));
            var root = json.RootElement;
            string? Value(string key) => root.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
            var name = Value("Name") ?? file.Split('/')[2];
            states.TryGetValue(name, out var disabled);
            plugins.Add(new(Value("ID"), name, "flow", Value("Version"), Value("Language"), Value("Website"),
                [name, file.Split('/')[2]], "FlowData/Settings/Plugins/" + name,
                disabled ?? (root.TryGetProperty("Disabled", out var flag) ? flag.ValueKind == JsonValueKind.True : null), root.Clone()));
            states.Remove(name);
        }
        foreach (var (name, disabled) in states)
            plugins.Add(new(null, name, "unattributed-flow", null, null, null, [name], "FlowData/Settings/Plugins/" + name, disabled));

        if (applicationDirectory != null)
        {
            using var installed = new SettingsBackupPaths(applicationDirectory);
            foreach (var file in installed.Files("Plugins").Where(p => Path.GetFileName(p).StartsWith("Lertaro.Plugins.", StringComparison.OrdinalIgnoreCase)
                         && p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)))
            {
                using var pinnedFile = installed.Read(file);
                // Reads assembly metadata without loading or executing the plugin (including failed plugins).
                AssemblyName assembly;
                try { assembly = AssemblyName.GetAssemblyName(installed.Resolve(file)); }
                catch (BadImageFormatException) { continue; }
                var key = Path.GetFileNameWithoutExtension(file);
                plugins.RemoveAll(p => p.Kind == "native" && p.Name == key);
                plugins.Add(new(assembly.Name, key, "native", assembly.Version?.ToString(), ".NET", file,
                    [key, Path.GetFileName(file)], null, null, DisabledComponents: Disabled(key)));
            }
        }
        return plugins;
    }
}
