using System.Text.Json;

namespace Lertaro.Core.Settings.Migration;

// Temporary upgrade bridge. Delete this file and its ReadLegacy/Upgrade call sites after the
// migration window. PluginSettingsStore never reads user-settings.json or writes legacy parameters.
internal static class LegacyPluginSettingsMigration
{
    internal static void ReadLegacy(string json, UserSettings settings)
    {
        if (settings.PluginSettingsStorageVersion != null) return;
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.TryGetProperty("PluginSettings", out var values))
            settings.PluginSettings = PluginSettingsStore.Parse(values.GetRawText());
    }

    internal static void Upgrade(string userSettingsPath, UserSettings settings)
    {
        var json = Prepare(userSettingsPath, settings, SettingsFileReader.ReadIfPresent(userSettingsPath));
        if (json != null && !UserSettingsPersistence.TryPersist(json, userSettingsPath))
            throw new IOException("Plugin settings were copied, but the migration could not finish updating user-settings.json. Retry without removing plugin-settings.json.");
    }

    internal static string? Prepare(string userSettingsPath, UserSettings settings, string? originalJson)
    {
        if (settings.PluginSettingsStorageVersion != null) return null;
        var path = PluginSettingsStore.PathFor(userSettingsPath);
        // A completed first write wins after interruption; never merge old values over new settings.
        if (!Enumerable.Range(0, 6).Any(index => SettingsFileReader.ReadIfPresent(index == 0 ? path : $"{path}.bak.{index}") != null))
            PluginSettingsStore.Save(userSettingsPath, settings.PluginSettings);
        settings.PluginSettings = PluginSettingsStore.Load(userSettingsPath);
        PluginSettingsStore.Save(userSettingsPath, settings.PluginSettings);
        settings.PluginSettingsStorageVersion = 1;
        settings.AdditionalSettings?.Remove("PluginSettings");
        System.Text.Json.Nodes.JsonObject? document = null;
        try { if (originalJson != null && UserSettings.TryParse(originalJson) != null) document = System.Text.Json.Nodes.JsonNode.Parse(originalJson) as System.Text.Json.Nodes.JsonObject; }
        catch (JsonException) { }
        document ??= JsonSerializer.SerializeToNode(settings)!.AsObject();
        document.Remove("PluginSettings");
        document["PluginSettingsStorageVersion"] = 1;
        return document.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }
}
