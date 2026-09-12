using Lertaro.App.Services;
using Lertaro.Core;
using Lertaro.Core.Wire;

namespace Lertaro.App.ViewModels.Settings.Plugins;

// Split out purely to keep PluginManagementViewModel under the repo's per-file line limit; this class
// has no state of its own, it always operates on the plugins handed to it. Owns writing staged plugin
// config fields back to settings and telling everything that reads them to re-read.
//
// Reached from the Settings window's Apply/OK (through PluginManagementViewModel.Save). Edits are staged
// per plugin and kept while the user switches around, so the commit walks EVERY plugin holding staged
// edits, not just whichever one happens to be on screen when Apply is pressed.
internal static class PluginConfigCommitSupport
{
    /// <summary>
    /// Commits every plugin holding staged config edits, persists the shared settings object once, and
    /// tells the hook process to re-read what changed.
    /// </summary>
    /// <remarks>
    /// Gated on <see cref="PluginInfoViewModel.HasPendingConfigEdits"/> rather than committing every
    /// plugin: a plugin's OnSave hook can be far from cheap (ContentSearch's triggers a full re-index),
    /// and a user who merely looked at a config without changing anything must not pay for it. The gate
    /// is also what keeps a plugin whose fields write straight through a custom SetValue (the Flow
    /// Launcher bridge) from re-writing every field on every Apply.
    ///
    /// The save has to happen here, before the ReloadSettings message below: the hook process re-reads
    /// the settings file on that message, so it would otherwise read the pre-edit values still on disk.
    /// </remarks>
    internal static void CommitPending(IEnumerable<PluginInfoViewModel> plugins)
    {
        UserSettings? settings = null;
        var committed = new List<PluginInfoViewModel>();

        foreach (var plugin in plugins)
        {
            if (plugin.ConfigFields.Count == 0 || !plugin.HasPendingConfigEdits)
                continue;

            foreach (var field in plugin.ConfigFields)
                field.Commit();

            settings ??= plugin.ConfigFields[0].Settings;
            committed.Add(plugin);
        }

        if (committed.Count == 0) return;

        settings!.Save();

        foreach (var plugin in committed)
            plugin.OnSave?.Invoke();

        InlineSearchManager.Instance.ExplorerTracker.RefreshActiveWindowAdapters();
        App.HookClient?.SendMessage(new IpcMessage { Id = IpcMessageId.ReloadSettings });
    }
}
