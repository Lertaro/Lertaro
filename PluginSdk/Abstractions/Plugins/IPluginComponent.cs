namespace Lertaro.PluginSdk.Abstractions.Plugins;

/// <summary>
/// Represents the base interface for all plugin components.
/// </summary>
public interface IPluginComponent
{
    /// <summary>
    /// The display name of this component.
    /// Defaults to the concrete type name.
    /// </summary>
    string Name => GetType().Name;

    /// <summary>
    /// A short description of this component.
    /// </summary>
    string Description => string.Empty;
}
