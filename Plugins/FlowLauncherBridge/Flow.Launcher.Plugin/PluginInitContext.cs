namespace Flow.Launcher.Plugin;

/// <summary>
/// Carries data passed to a plugin when it gets initialized.
/// </summary>
public class PluginInitContext
{
    public PluginInitContext()
    {
    }

    public PluginInitContext(PluginMetadata currentPluginMetadata, IPublicAPI api)
    {
        CurrentPluginMetadata = currentPluginMetadata;
        API = api;
    }

    public PluginMetadata CurrentPluginMetadata { get; internal set; } = null!;
    public IPublicAPI API { get; set; } = null!;
}
