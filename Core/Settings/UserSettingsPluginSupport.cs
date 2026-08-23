namespace Lertaro.Core;

/// <summary>
/// Helper methods managing plugin setting lookups and updates on UserSettings instances.
/// Split out from UserSettings.cs to keep file size strictly under the repo line limit.
/// </summary>
public static class UserSettingsPluginSupport
{
    public static T GetPluginSetting<T>(UserSettings settings, string pluginId, string key, T defaultValue)
    {
        if (settings.PluginSettings.TryGetValue(pluginId, out var settingsDict) && settingsDict.TryGetValue(key, out var val))
        {
            try
            {
                if (val is T typedVal)
                {
                    return typedVal;
                }
                if (val is System.Text.Json.JsonElement element)
                {
                    return System.Text.Json.JsonSerializer.Deserialize<T>(element.GetRawText())!;
                }
                return (T)Convert.ChangeType(val, typeof(T));
            }
            catch
            {
                return defaultValue;
            }
        }
        return defaultValue;
    }

    public static void SetPluginSetting(UserSettings settings, string pluginId, string key, object? value)
    {
        if (!settings.PluginSettings.TryGetValue(pluginId, out var settingsDict))
        {
            settingsDict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            settings.PluginSettings[pluginId] = settingsDict;
        }
        if (value == null)
        {
            settingsDict.Remove(key);
        }
        else
        {
            settingsDict[key] = value;
        }

        PluginSdk.Services.PluginSettingsService.NotifySettingChanged(pluginId, key, value);
    }
}
