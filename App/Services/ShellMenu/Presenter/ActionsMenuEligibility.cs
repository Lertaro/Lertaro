using Lertaro.App.Services.Plugin;
using Lertaro.PluginSdk.Abstractions.Plugins;

namespace Lertaro.App.Services.ShellMenu.Presenter;

/// <summary>
/// The part of "may the actions menu open for this selection" that is about INSTANT results.
/// </summary>
/// <remarks>
/// Split out of <see cref="ShellMenuPresenter"/> purely to keep that file under the repo's per-file line
/// limit; it has no state and always operates on the providers and selection it is handed.
///
/// Instant results (the window switcher's windows, a calculator's answer, ...) carry no file path, so the
/// host keeps the actions menu closed for them unless a provider declares that it acts on them -- see
/// <see cref="IDynamicActionProvider.CanProvideForInstantResults"/>. The providers arrive as a parameter
/// rather than being read from <see cref="PluginManager"/> here so the rule can be pinned by a test.
/// </remarks>
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
