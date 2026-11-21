using Lertaro.App.Services;
using Lertaro.Core;
using Lertaro.Core.Wire;

namespace Lertaro.App.ViewModels.Settings.Plugins;

// Commits staged configuration and notifies consumers on Settings Apply/OK. Visit every plugin with
// edits, including plugins whose tabs are no longer visible.
internal static class PluginConfigCommitSupport
{
    /// <summary>
    /// Commits every plugin holding staged config edits, persists the shared settings object once, and
    /// tells the hook process to re-read what changed.
    /// </summary>
    /// <remarks>
    /// Field commits are gated on <see cref="PluginInfoViewModel.HasPendingConfigEdits"/> rather than committing every
    /// plugin: a plugin's OnSave hook can be far from cheap (ContentSearch's triggers a full re-index),
    /// and merely viewing ordinary fields must not trigger it. Opened custom panels still need their
    /// OnSave hook because their private models do not report dirty state. The field gate
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
            if (!plugin.HasPendingConfigEdits)
            {
                // A hosted settings panel edits its own model, outside our value-field dirty flags.
                // Persist that model without rewriting untouched host-managed fields.
                if (plugin.RequiresCustomConfigSave) plugin.OnSave?.Invoke();
                continue;
            }

            foreach (var field in plugin.ConfigFields)
                field.Commit();

            settings ??= plugin.ConfigFields[0].Settings;
            committed.Add(plugin);
        }

        if (committed.Count == 0) return;

        if (!settings!.Save())
            throw new System.IO.IOException("Could not save plugin settings.");

        foreach (var plugin in committed)
            plugin.OnSave?.Invoke();

        InlineSearchManager.Instance.ExplorerTracker.RefreshActiveWindowAdapters();
        App.HookClient?.SendMessage(new IpcMessage { Id = IpcMessageId.ReloadSettings });
    }
}
