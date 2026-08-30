namespace Lertaro.PluginSdk.Services;

/// <summary>
/// Requests that the host display its settings window.
/// </summary>
public static class SettingsWindowService
{
    /// <summary>Delegate set by the host to show a settings section.</summary>
    public static Func<string?, bool>? ShowWindowFunc { get; set; }

    /// <summary>Delegate set by the host to show a specific searchable settings entry.</summary>
    public static Func<SettingsSearchEntryInfo, bool>? ShowEntryFunc { get; set; }

    /// <summary>Shows the settings window, optionally selecting a section.</summary>
    public static bool ShowWindow(string? targetSection = null)
    {
        try { return ShowWindowFunc?.Invoke(targetSection) ?? false; }
        catch { return false; }
    }

    /// <summary>Shows the settings window and selects the supplied searchable entry.</summary>
    public static bool ShowEntry(SettingsSearchEntryInfo? entry)
    {
        if (entry == null)
            return false;

        try { return ShowEntryFunc?.Invoke(entry) ?? false; }
        catch { return false; }
    }
}
