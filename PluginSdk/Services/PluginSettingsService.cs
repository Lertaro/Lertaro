namespace Lertaro.PluginSdk.Services;

/// <summary>
/// A decoupled service to provide read access to plugin-specific settings from the host application.
/// </summary>
public static class PluginSettingsService
{
    /// <summary>
    /// Delegate function set by the host application to retrieve plugin settings.
    /// Parameters: (pluginId, settingKey, defaultValue)
    /// </summary>
    public static Func<string, string, object?, object?>? GetSettingFunc { get; set; }

    /// <summary>
    /// Delegate action set by the host application to persist a plugin setting written from plugin
    /// code itself (as opposed to the Settings UI's own edit/apply flow). Parameters: (pluginId,
    /// settingKey, value) -- a null value removes the key. Not every plugin needs this: most settings
    /// are only ever edited through their own Configure dialog, so this stays unset (SetSetting is then
    /// a silent no-op) unless the host wires it up.
    /// </summary>
    public static Action<string, string, object?>? SetSettingFunc { get; set; }

    /// <summary>
    /// Event raised when a plugin setting is updated.
    /// Parameters: (pluginId, key)
    /// </summary>
    public static event Action<string, string>? SettingChanged;

    /// <summary>
    /// Notifies listeners that a specific plugin setting has changed.
    /// </summary>
    public static void NotifySettingChanged(string pluginId, string key) => SettingChanged?.Invoke(pluginId, key);

    /// <summary>
    /// Retrieves a setting value for a specific plugin.
    /// </summary>
    public static T GetSetting<T>(string pluginId, string key, T defaultValue)
    {
        if (GetSettingFunc == null) return defaultValue;
        try
        {
            var val = GetSettingFunc(pluginId, key, defaultValue);
            if (val is T typedVal) return typedVal;
            if (val != null)
            {
                if (val is System.Text.Json.JsonElement element)
                {
                    return System.Text.Json.JsonSerializer.Deserialize<T>(element.GetRawText())!;
                }

                try
                {
                    return (T)Convert.ChangeType(val, typeof(T));
                }
                catch
                {
                    var json = System.Text.Json.JsonSerializer.Serialize(val);
                    return System.Text.Json.JsonSerializer.Deserialize<T>(json)!;
                }
            }
        }
        catch
        {
            // Fallback to default
        }
        return defaultValue;
    }

    /// <summary>
    /// Persists a setting value for a specific plugin, immediately (not batched with anything else the
    /// Settings UI might be mid-editing) -- meant for a plugin writing back its own setting in response
    /// to something the user did at runtime (e.g. a "add current folder" quick-navigation command),
    /// not for mirroring every keystroke of a text field.
    /// </summary>
    public static void SetSetting(string pluginId, string key, object? value) => SetSettingFunc?.Invoke(pluginId, key, value);
}
