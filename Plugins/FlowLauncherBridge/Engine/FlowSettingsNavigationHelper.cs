using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.FlowLauncherBridge.Engine;

/// <summary>Opens the host settings page at a Flow plugin's generated configuration group.</summary>
internal static class FlowSettingsNavigationHelper
{
    internal static bool OpenPluginSettings(string pluginName)
    {
        var entry = FindPluginConfigEntry(pluginName);
        return entry == null
            ? SettingsWindowService.ShowWindow("Plugins")
            : SettingsWindowService.ShowEntry(entry);
    }

    private static SettingsSearchEntryInfo? FindPluginConfigEntry(string pluginName)
    {
        if (string.IsNullOrWhiteSpace(pluginName))
            return null;

        try
        {
            return FindPluginConfigEntry(pluginName, SettingsSearchService.GetEntries());
        }
        catch
        {
            return null;
        }
    }

    internal static SettingsSearchEntryInfo? FindPluginConfigEntry(
        string pluginName, IEnumerable<SettingsSearchEntryInfo> entries)
    {
        if (string.IsNullOrWhiteSpace(pluginName))
            return null;

        var nameSegment = $" › {pluginName}";
        var fieldEntry = entries.FirstOrDefault(entry =>
            entry.Breadcrumb.EndsWith(nameSegment, StringComparison.OrdinalIgnoreCase));
        if (fieldEntry != null)
            return fieldEntry;

        // A Flow plugin always contributes a top-level config group. If that group has no visible
        // child field, its own entry is the only searchable item that can reveal the config tab.
        return entries.FirstOrDefault(entry =>
            string.Equals(entry.Label, pluginName, StringComparison.OrdinalIgnoreCase)
            && entry.Breadcrumb.Contains(" › ", StringComparison.Ordinal));
    }
}
