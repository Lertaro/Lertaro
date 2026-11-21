using Lertaro.App.Services.Plugin;
using Lertaro.PluginSdk.Abstractions.Plugins;

namespace Lertaro.App.Services.ShellMenu.Presenter;

/// <summary>
/// Determines whether instant results support the actions menu using the supplied providers and selection.
/// </summary>
internal static class ActionsMenuEligibility
{
    /// <summary>
    /// Whether an instant-result selection may open the actions menu. Any provider that declares it acts
    /// on instant results AND claims this particular selection is enough.
    /// </summary>
    public static bool AllowsInstantResults(
        IEnumerable<IDynamicActionProvider> providers, IReadOnlyList<AppSearchResult> selection)
    {
        foreach (var provider in providers)
        {
            // The declaration is consulted first and is the cheap half: CanProvide is allowed to do real
            // work in a provider, and only a provider that opted in needs to be asked.
            if (!provider.CanProvideForInstantResults) continue;

            try
            {
                if (provider.CanProvide(selection)) return true;
            }
            catch (Exception ex)
            {
                // A throwing provider must not take down the keyboard path that asked; it simply gets no
                // say, the same way a failing provider is skipped everywhere else in this menu.
                Core.Logger.Log($"[ActionsMenuEligibility] Provider '{provider.Name}' threw in CanProvide: {ex.Message}", Core.LogLevel.Error);
            }
        }

        return false;
    }
}
