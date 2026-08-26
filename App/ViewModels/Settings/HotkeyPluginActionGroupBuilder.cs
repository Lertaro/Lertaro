using Lertaro.App.Services.Plugin;
using Lertaro.App.Services.PluginManagerCore;

namespace Lertaro.App.ViewModels.Settings;

// Split out of HotkeySettingsViewModel to keep that file under the repo's per-file line limit;
// this class has no state of its own, it always builds from the given overrides.
internal static class HotkeyPluginActionGroupBuilder
{
    // Same groups (in the same PluginManager.Instance.AllActions order) for both the live
    // HotkeySettingsViewModel and the settings-search feed, which never has a live settings VM.
    internal static List<PluginActionGroupViewModel> Build(Dictionary<string, Dictionary<string, string>> overrides)
    {
        var groups = new List<PluginActionGroupViewModel>();
        foreach (var pluginGroup in PluginManager.Instance.AllActions.GroupBy(r => r.Plugin))
        {
            // Matches the plugin ID convention already used by PluginSettings/PluginConfigFieldViewModel:
            // the DLL file name with its extension stripped (e.g. "Lertaro.Plugins.CoreExtensions").
            var pluginId = System.IO.Path.GetFileNameWithoutExtension(ComponentFilter.GetDllName(pluginGroup.Key));
            // Excludes actions that declare Parameters (e.g. TouchAction/MkdirAction's "filename"/
            // "foldername") -- those only make sense invoked by typing their Keywords ("touch foo.txt"),
            // which is where the actual argument comes from. A hotkey press has no such text to supply,
            // and these actions don't override IsVisibleInMenu to offer a parameter-free fallback the way
            // OpenCommandPromptAction does (its own Keywords-based "cmd"/"cmda" typing is one path, but it
            // also stays hotkey-able whenever a single folder is selected) -- so a configured hotkey for
            // one of these would sit in Settings looking functional while never actually firing, in any
            // window, because HotkeyActionTrigger's dispatch (App\Helpers\HotkeyActionTrigger.cs) requires
            // IsVisibleInMenu to be true, whose default implementation is `Keywords.Count == 0`.
            var hotkeyConfigurableActions = pluginGroup.Where(reg => reg.Action.Parameters.Count == 0);
            var items = hotkeyConfigurableActions.Select(reg =>
            {
                var currentValue = overrides.TryGetValue(pluginId, out var pluginOverrides)
                     && pluginOverrides.TryGetValue(reg.Action.GetType().Name, out var overrideValue)
                    ? overrideValue
                    : reg.Action.Hotkey;
                return new PluginActionHotkeyItemViewModel(pluginId, reg.Action, currentValue);
            }).ToList();

            if (items.Count > 0)
                groups.Add(new PluginActionGroupViewModel(pluginGroup.Key, items));
        }
        return groups;
    }
}
