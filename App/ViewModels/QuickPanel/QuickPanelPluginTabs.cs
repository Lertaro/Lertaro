using Lertaro.App.Services.Plugin;
using Lertaro.PluginSdk.Abstractions.Plugins;

namespace Lertaro.App.ViewModels.QuickPanel;

/// <summary>The plugin-provided tabs, and the id each is known by.</summary>
/// <remarks>
/// One place, because three surfaces have to agree on that id: the settings page listing what a plugin
/// offers, the settings file remembering which ones were closed and in what order the strip runs, and
/// the panel looking a provider back up when it loads.
/// </remarks>
internal static class QuickPanelPluginTabs
{
    /// <summary>Every provider the plugin layer currently offers, disabled components already dropped.</summary>
    public static IEnumerable<IQuickPanelTabProvider> Available => PluginManager.Instance.QuickPanelTabProviders;

    /// <summary>The id a provider is stored under: its dll, its kind, and its type name.</summary>
    public static string ComponentId(IQuickPanelTabProvider provider)
        => $"{System.IO.Path.GetFileNameWithoutExtension(provider.GetType().Assembly.Location)}::QuickPanelTabProvider::{provider.GetType().Name}";

    /// <summary>The provider behind an id, or null when there is not one any more.</summary>
    /// <remarks>
    /// A stored id can name a plugin that has since been uninstalled or switched off. The id stays in the
    /// settings rather than being pruned -- the same rule the order follows, so a plugin turned off for a
    /// week comes back where the user left it rather than at the end.
    /// </remarks>
    public static IQuickPanelTabProvider? Find(string componentId)
        => Available.FirstOrDefault(p => ComponentId(p).Equals(componentId, StringComparison.OrdinalIgnoreCase));
}
