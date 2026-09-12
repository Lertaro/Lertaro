namespace Lertaro.App.ViewModels.Settings.Plugins;

// Owns the staged-edit lifecycle of one plugin's config tab -- what to do when it is opened and when it
// is left. Split out purely to keep PluginInfoViewModel under the repo's per-file line limit; it holds
// only the flags describing that lifecycle and always operates on the one plugin it is handed.
//
// The shape keeps the plugin-SELECTION path cheap. Staged edits must be rolled back whenever the config
// tab goes away, but rebuilding the schema tree there is wasted work -- nothing is displaying it. So
// leaving only DROPS the rows and remembers that; the rebuild happens when the tab is next opened.
// Measured before: switching away from a plugin whose config had ever been opened rebuilt its whole tree
// (~45ms for the 54-component plugin) on every single switch.
internal sealed class PluginConfigTabState
{
    private readonly PluginInfoViewModel _plugin;

    // 0 = never opened, 1 = open, -1 = dropped and owed a rebuild before the next open.
    private int _state;

    internal PluginConfigTabState(PluginInfoViewModel plugin) => _plugin = plugin;

    /// <summary>Called when the config tab is shown.</summary>
    internal void Opened()
    {
        var needsRebuild = _state == -1;
        _state = 1;

        // Only a tree dropped on the way out needs rebuilding; a first open reads settings through the
        // fields' own lazy getters, which is the whole point of the laziness.
        if (!needsRebuild) return;

        foreach (var field in _plugin.ConfigFields)
            field.Reload();
    }

    /// <summary>
    /// Called when the config tab is left: discards staged edits, cheaply. A no-op when nothing was ever
    /// staged (edits are only possible while the tab is open), and when it was already discarded -- a
    /// selection change both rolls the outgoing plugin back and clears its tab flag, and doing the work
    /// twice per click was the old behaviour.
    /// </summary>
    /// <returns>Whether anything was discarded (the caller then re-selects the first config group).</returns>
    internal bool Closed()
    {
        if (_state == 0 || _state == -1) return false;
        _state = -1;

        foreach (var field in _plugin.ConfigFields)
            field.Discard();
        return true;
    }
}
