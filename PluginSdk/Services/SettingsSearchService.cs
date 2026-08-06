namespace Lertaro.PluginSdk.Services;

/// <summary>
/// A decoupled service that exposes the host's static list of searchable settings entries to plugins,
/// so a plugin can offer its own "jump straight to a specific setting" feature without a direct
/// reference to the host app (plugins can't reference the App project).
/// </summary>
public static class SettingsSearchService
{
    /// <summary>
    /// Delegate function set by the main application to enumerate every currently-reachable settings
    /// entry. Each entry's Index round-trips through lertaro://settings/entry/&lt;index&gt; to jump
    /// straight to it (see UriRouter in the host app).
    /// </summary>
    public static Func<IReadOnlyList<SettingsSearchEntryInfo>> GetEntriesFunc { get; set; } = () => Array.Empty<SettingsSearchEntryInfo>();

    /// <summary>
    /// Gets every currently-reachable settings entry.
    /// </summary>
    public static IReadOnlyList<SettingsSearchEntryInfo> GetEntries() => GetEntriesFunc();
}

/// <summary>
/// One searchable settings entry, as exposed to plugins.
/// </summary>
/// <param name="Label">The setting's translated display label.</param>
/// <param name="Breadcrumb">The translated "Section &gt; Tab &gt; SubTab" path this entry lives under.</param>
/// <param name="Index">This entry's position in the host's internal list -- pass back via
/// lertaro://settings/entry/&lt;index&gt; to jump straight to it. Not stable across app restarts or
/// settings-page changes; only meaningful within the same running process's list of entries.</param>
public sealed record SettingsSearchEntryInfo(string Label, string Breadcrumb, int Index);
