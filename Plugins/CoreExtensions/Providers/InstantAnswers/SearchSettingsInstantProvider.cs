using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.CoreExtensions.Providers.InstantAnswers;

// Lets the main search box jump straight to a specific setting (section + tab + row, highlighted),
// without opening Settings first and using its own internal search box. SettingsSearchService exposes
// the host's SettingsSearchIndex.Entries -- this plugin can't reference App directly, so it has no
// other way to see what settings exist. Selecting a result hands the entry's index back via
// lertaro://settings/entry/<index> (see UriRouter in the host app), resolved through the OS via the
// lertaro:// protocol registration -- the same "Execute" + URL pattern WebSearchInstantProvider
// already uses for its own results, just pointed at ourselves instead of a browser.
public class SearchSettingsInstantProvider : IInstantResultProvider
{
    private const string PluginId = "Lertaro.Plugins.CoreExtensions";
    private const string DefaultTriggerWord = "set";

    static SearchSettingsInstantProvider() =>
        // Invalidate the cached trigger word as soon as the host reports this plugin's settings were
        // saved, so a changed trigger applies to the very next keystroke instead of requiring a restart.
        PluginSettingsService.SettingChanged += (pluginId, key) =>
        {
            if (string.Equals(pluginId, PluginId, StringComparison.OrdinalIgnoreCase))
                _cachedTrigger = null;
        };

    private static string? _cachedTrigger;

    private static string GetTriggerPrefix()
    {
        // "trigger word" + " ", matching WebSearchInstantProvider's own prefix convention.
        _cachedTrigger ??= PluginSettingsService.GetSetting(PluginId, "SearchSettingsTrigger", DefaultTriggerWord).Trim();
        return (_cachedTrigger.Length > 0 ? _cachedTrigger : DefaultTriggerWord) + " ";
    }

    public string Name => TranslationService.Get("SearchSettings_Name");

    public IEnumerable<InstantResultItem> GetInstantResults(string query)
    {
        var trigger = GetTriggerPrefix();
        if (string.IsNullOrEmpty(query) || !query.StartsWith(trigger, StringComparison.OrdinalIgnoreCase))
            yield break;

        var term = query.Substring(trigger.Length).Trim();
        var browseAll = term.Length == 0;

        foreach (var entry in SettingsSearchService.GetEntries())
        {
            if (!browseAll && !FuzzyMatchService.IsMatch(term, entry.Label))
                continue;

            yield return new InstantResultItem
            {
                Title = entry.Label,
                Description = entry.Breadcrumb,
                IconData = "M19.14 12.94c.04-.3.06-.61.06-.94 0-.32-.02-.64-.07-.94l2.03-1.58c.18-.14.23-.41.12-.61l-1.92-3.32c-.12-.22-.37-.29-.59-.22l-2.39.96c-.5-.38-1.03-.7-1.62-.94l-.36-2.54c-.04-.24-.24-.41-.48-.41h-3.84c-.24 0-.43.17-.47.41l-.36 2.54c-.59.24-1.13.57-1.62.94l-2.39-.96c-.22-.08-.47 0-.59.22L2.74 8.87c-.12.21-.08.47.12.61l2.03 1.58c-.05.3-.09.63-.09.94s.02.64.07.94l-2.03 1.58c-.18.14-.23.41-.12.61l1.92 3.32c.12.22.37.29.59.22l2.39-.96c.5.38 1.03.7 1.62.94l.36 2.54c.05.24.24.41.48.41h3.84c.24 0 .44-.17.47-.41l.36-2.54c.59-.24 1.13-.56 1.62-.94l2.39.96c.22.08.47 0 .59-.22l1.92-3.32c.12-.22.07-.47-.12-.61l-2.01-1.58zM12 15.6c-1.98 0-3.6-1.62-3.6-3.6s1.62-3.6 3.6-3.6 3.6 1.62 3.6 3.6-1.62 3.6-3.6 3.6z",
                IconColor = "DefaultPluginIconColor",
                ActionType = "Execute",
                ActionArgument = $"lertaro://settings/entry/{entry.Index}",
                TabCompletion = trigger + entry.Label
            };
        }
    }

    public bool[]? GetHighlightMask(string text, string query)
    {
        var trigger = GetTriggerPrefix();
        if (string.IsNullOrEmpty(query) || !query.StartsWith(trigger, StringComparison.OrdinalIgnoreCase))
            return null;

        var term = query.Substring(trigger.Length).Trim();
        var mask = new bool[text.Length];
        if (term.Length == 0)
            return mask;

        return FuzzyMatchService.GetHighlightMask(text, term) ?? mask;
    }
}
