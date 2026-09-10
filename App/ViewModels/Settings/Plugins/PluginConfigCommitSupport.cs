using Lertaro.App.Services;
using Lertaro.Core.Wire;

namespace Lertaro.App.ViewModels.Settings.Plugins;

// Split out purely to keep PluginManagementViewModel under the repo's per-file line limit; this class
// has no state of its own, it always operates on the plugin handed to it. Owns writing a plugin's staged
// config fields back to settings and telling everything that reads them to re-read. Reached from two
// places -- the plugin page's own Save Config button, and the Settings window's Apply/OK -- so the user
// no longer has to press the page's button as a second step after Apply.
internal static class PluginConfigCommitSupport
{
    // Which plugin's staged config, if any, the Settings window's Apply/OK should write. Only one whose
    // config tab is open: that tab is the only place edits are staged, and leaving it rolls the fields
    // back (see PluginInfoViewModel.IsConfigTab), so a closed tab has nothing pending to save.
    internal static PluginInfoViewModel? PendingOnSettingsApply(PluginInfoViewModel? selectedPlugin) =>
        selectedPlugin is { IsConfigTab: true } ? selectedPlugin : null;

    // Commits every field, persists once through the settings object they share, and tells the hook
    // process to re-read what just changed.
    internal static void Commit(PluginInfoViewModel? plugin)
    {
        if (plugin == null || plugin.ConfigFields.Count == 0) return;

        foreach (var field in plugin.ConfigFields)
            field.Commit();

        plugin.ConfigFields[0].Settings.Save();
        plugin.OnSave?.Invoke();
        InlineSearchManager.Instance.ExplorerTracker.RefreshActiveWindowAdapters();
        App.HookClient?.SendMessage(new IpcMessage { Id = IpcMessageId.ReloadSettings });
    }
}
