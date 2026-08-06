namespace Lertaro.PluginSdk.Abstractions.Plugins;

/// <summary>
/// Represents a plugin or component that provides search result actions.
/// </summary>
public interface IActionProvider
{
    /// <summary>Returns the actions provided by this plugin.</summary>
    IEnumerable<ISearchResultAction> GetActions();

    /// <summary>Returns the dynamic action providers (e.g. Shell Context Menu) provided by this plugin.</summary>
    IEnumerable<IDynamicActionProvider> GetDynamicActionProviders();
}
