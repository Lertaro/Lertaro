namespace Lertaro.PluginSdk.Abstractions.Plugins;

/// <summary>
/// Represents the base interface for all plugins.
/// </summary>
public interface IPlugin : IPluginComponent
{
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
