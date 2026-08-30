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

        var suffix = $" › {pluginName}";
        return entries.FirstOrDefault(entry =>
            entry.Breadcrumb.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
    }
}
