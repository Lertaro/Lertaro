using Lertaro.App.Services.Plugin;
using Lertaro.Core;
using Lertaro.PluginSdk.Abstractions.Plugins;

namespace Lertaro.App.Helpers;

/// <summary>
/// Asks the loaded plugins to describe a day in calendar terms, for the quick window's clock line.
/// </summary>
/// <remarks>
/// The 农历 date, the 节气 and the festival names come from the Calendar plugin's tables; the host carries no
/// copy of them (see <see cref="ICalendarTextProvider"/>). The first provider that answers wins, so a second
/// calendar plugin with its own conventions contributes the text rather than racing the first one's.
/// </remarks>
internal static class CalendarTextService
{
    /// <summary>
    /// The plugin-supplied description of <paramref name="date"/>, or an empty string when no provider has
    /// anything to say.
    /// </summary>
    /// <remarks>
    /// Asks <see cref="PluginManager.CalendarTextProviders"/>, which is the ENABLED projection: a plugin the
    /// user disabled, or a calendar-text component switched off under it, is filtered out there and so is
    /// never called here. That is the whole of "a disabled plugin is not asked" -- there is no second check
    /// on this side, and deliberately so: one gate, in one place, is what makes the answer the same for every
    /// consumer.
    /// </remarks>
    internal static string Describe(DateTime date) => Describe(PluginManager.Instance.CalendarTextProviders, date);

    /// <summary>
    /// The same, over a caller-supplied set of providers.
    /// </summary>
    /// <param name="providers">
    /// Whom to ask, in order. Split out from the overload above so the asking rules can be tested without a
    /// loaded plugin: an empty sequence is what a disabled plugin leaves behind, and a provider that throws
    /// is third-party code on the UI thread.
    /// </param>
    /// <param name="date">The day to describe.</param>
    internal static string Describe(IEnumerable<ICalendarTextProvider> providers, DateTime date)
    {
        foreach (var provider in providers)
        {
            try
            {
                var text = provider.GetCalendarText(date);
                if (!string.IsNullOrWhiteSpace(text))
                    return text;
            }
            catch (Exception ex)
            {
                // Third-party code on the UI thread, on a path the user triggers by clearing the search box:
                // one broken provider must not take the clock line, or the window, down with it.
                Logger.Log($"[CalendarText] Provider '{provider.GetType().Name}' failed to describe {date:d}: {ex.Message}", LogLevel.Warn);
            }
        }

        return string.Empty;
    }
}
