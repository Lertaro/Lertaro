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
    internal static string Describe(DateTime date)
    {
        foreach (var provider in PluginManager.Instance.CalendarTextProviders)
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
