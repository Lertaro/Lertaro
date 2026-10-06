namespace Lertaro.PluginSdk.Abstractions.Plugins;

/// <summary>
/// Represents the base interface for all plugins.
/// </summary>
public interface IPlugin : IPluginComponent
{
    /// <summary>
    /// Flush pending settings and stop external writers before a settings-transfer restart.
    /// Optional for existing plugins; the host still inventories disk data for unloaded plugins.
    /// Throw on failure so the transfer is cancelled rather than reported as a complete backup.
    /// </summary>
    Task PrepareForSettingsTransferAsync() => Task.CompletedTask;

    /// <summary>
    /// Optional website, repository, or plugin store URL for this plugin.
    /// When set, a clickable hyperlink is displayed on the plugin's details card in settings.
    /// </summary>
    string? WebsiteUrl => null;

    /// <summary>
    /// Optional display label for the website link (e.g., "Browse Plugins").
    /// Defaults to a localized "Visit website" label when not specified.
    /// </summary>
    string? WebsiteLabel => null;
}
