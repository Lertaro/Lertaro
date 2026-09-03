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
    /// Delegate set by the host to query whether a plugin component is enabled. The component type
    /// uses the host's stable enum name, and the assembly name includes its .dll extension.
    /// </summary>
    public static Func<string, string, string, bool>? IsComponentEnabledFunc { get; set; }

    /// <summary>Raised by the host after the persisted plugin component enablement has changed.</summary>
    public static event Action? ComponentEnablementChanged;

    /// <summary>
    /// Event raised when a plugin setting is updated.
    /// Parameters: (pluginId, key)
    /// </summary>
    public static event Action<string, string>? SettingChanged;

    /// <summary>
    /// Event raised when a plugin setting is updated with its new value.
    /// Parameters: (pluginId, key, value)
    /// </summary>
    public static event Action<string, string, object?>? SettingChangedWithValue;

    /// <summary>
    /// Notifies listeners that a specific plugin setting has changed.
    /// </summary>
    public static void NotifySettingChanged(string pluginId, string key, object? value = null)
    {
        SettingChanged?.Invoke(pluginId, key);
        SettingChangedWithValue?.Invoke(pluginId, key, value);
    }

    /// <summary>Notifies plugin runtimes that component enablement has been refreshed.</summary>
    public static void NotifyComponentEnablementChanged() => ComponentEnablementChanged?.Invoke();

    /// <summary>Returns whether the host currently allows a plugin component to run.</summary>
    public static bool IsComponentEnabled(string dllName, string componentType, string componentName)
    {
        if (IsComponentEnabledFunc == null) return true;
        try { return IsComponentEnabledFunc(dllName, componentType, componentName); }
        catch { return true; }
    }

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
