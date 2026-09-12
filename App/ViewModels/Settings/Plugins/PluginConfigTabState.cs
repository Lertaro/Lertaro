namespace Lertaro.App.ViewModels.Settings.Plugins;

// Owns the staged-edit lifecycle of one plugin's config tab -- what to do when it is opened, left, or
// rolled back. Split out purely to keep PluginInfoViewModel under the repo's per-file line limit; it
// holds only the one lifecycle flag (never opened / open / left) and always operates on the one plugin
// it is handed.
//
// Leaving the tab (or switching to another plugin) no longer discards staged edits: the Settings
// window's Apply/OK is the only commit point, so a user who steps away and comes back must still see
// what they typed. What leaving DOES do is drop the rows of fields with no staged edit -- nothing is
// displaying them, and a clean field's tree carries nothing the lazy getters cannot rebuild identically.
// A field that actually holds an edit keeps its tree, because that tree is where the edit lives.
//
// Staged edits are discarded by DiscardStagedEdits, reached only from Cancel (SettingsViewModel.Cleanup
// -> RollbackConfig) -- not from switching tabs or plugins.
internal sealed class PluginConfigTabState
{
    private readonly PluginInfoViewModel _plugin;

    // 0 = never opened, 1 = open, -1 = left, with the clean fields' rows dropped and owed a rebuild
    // before the next open.
    private int _state;

    internal PluginConfigTabState(PluginInfoViewModel plugin) => _plugin = plugin;

    /// <summary>Called when the config tab is shown.</summary>
    internal void Opened()
    {
        var needsRebuild = _state == -1;
        _state = 1;

        // Only a tree dropped on the way out needs rebuilding; a first open reads settings through the
        // fields' own lazy getters, which is the whole point of the laziness. RebuildRowsIfClean skips
        // fields holding an edit, whose rows were kept on the way out precisely so it would have nothing
        // to rebuild (and nothing to lose).
        if (!needsRebuild) return;

        foreach (var field in _plugin.ConfigFields)
            field.RebuildRowsIfClean();
    }

    /// <summary>
    /// Called when the config tab is left (tab switch, plugin switch, or window teardown): drops the rows
    /// of fields with no staged edit, keeping the trees that hold one. A no-op when the tab was never
    /// opened, and when it was already left.
    /// </summary>
    internal void Closed()
    {
        if (_state == 0 || _state == -1) return;
        _state = -1;

        foreach (var field in _plugin.ConfigFields)
            field.DiscardRowsIfClean();
    }

    /// <summary>
    /// Discards every staged edit on this plugin, for when the user cancelled or an apply is being
    /// undone. A no-op when the config tab was never opened, since edits are only possible while it was.
    /// </summary>
    /// <returns>Whether anything was discarded (the caller then re-selects the first config group).</returns>
    internal bool DiscardStagedEdits()
    {
        if (_state == 0) return false;

        // A rollback with the tab still showing (a direct RollbackConfig call, or Cancel on a window that
        // still has it up) must rebuild so the bound controls re-read the persisted values; leaving it as
        // a plain drop would blank them until the section were recreated.
        var wasOpen = _state == 1;
        foreach (var field in _plugin.ConfigFields)
        {
            if (wasOpen) field.Reload();
            else field.Discard();
        }

        _state = wasOpen ? 1 : -1;
        return true;
    }
}
