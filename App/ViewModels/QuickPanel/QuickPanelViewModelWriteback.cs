namespace Lertaro.App.ViewModels.QuickPanel;

// Persists tab closure and tab order. Group sorting, view mode, and collapsed state remain session-only.
//
// The Settings page edits a staged copy, so saving it after a panel change can overwrite that change; the
// last save wins.
public partial class QuickPanelViewModel
{
    /// <summary>Writes the strip's order down as the order it now is.</summary>
    /// <remarks>
    /// One list over both kinds of tab, which is what makes a plugin tab draggable to between two
    /// workspaces at all. It records only what is in the strip: a disabled workspace or a closed plugin
    /// tab has nothing to drag and keeps whatever position it had, since an id nobody has ordered falls
    /// back to discovery order rather than to the front.
    /// </remarks>
    private void PersistTabOrder()
    {
        // A drag is a remove followed by an insert, so the strip is briefly one tab short. That halfway
        // state is not an order worth writing, and it is exactly the one where the counts disagree.
        if (_rebuildingTabs || Tabs.Count != _tabs.Count) return;

        var settings = _readSettings();
        var strip = Tabs.Select(tab => tab.Id).ToList();

        // Everything previously ordered that is not on screen right now keeps its relative place after
        // the strip: it was ordered once and nothing here is a statement about it.
        var rest = settings.TabOrder.Where(id => !strip.Contains(id, StringComparer.OrdinalIgnoreCase));

        settings.TabOrder = strip.Concat(rest).ToList();
        _tabs = strip
            .Select(id => _tabs.First(tab => tab.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        _saveSettings();
    }

    /// <summary>Takes a tab out of the strip, by whatever "closed" means for the thing behind it.</summary>
    /// <remarks>
    /// Never deleted. A workspace is disabled, keeping the source list that took assembling; a plugin tab
    /// is recorded as closed, which is a statement about the strip rather than about the plugin (that
    /// one lives on the Plugins page and stops it loading at all). Both come back from Settings > Quick
    /// Panel.
    /// </remarks>
    public Task CloseTabAsync(string tabId, CancellationToken token = default)
    {
        var tab = _tabs.FirstOrDefault(candidate => candidate.Id.Equals(tabId, StringComparison.OrdinalIgnoreCase));
        if (tab == null) return Task.CompletedTask;

        var settings = _readSettings();
        tab.Close(settings);
        _saveSettings();

        // Dropped from what is already loaded rather than reloaded: closing a tab says nothing about what
        // the others hold, and the panel is open while this happens.
        _content.Remove(tabId);
        _tabs = _tabs.Where(candidate => !string.Equals(candidate.Id, tabId, StringComparison.OrdinalIgnoreCase)).ToList();

        // Closing the tab being looked at leaves the panel on the first one still there, rather than on
        // one that no longer has a tab to reach it by.
        if (!_tabs.Any(candidate => string.Equals(candidate.Id, _activeTabId, StringComparison.OrdinalIgnoreCase)))
            _activeTabId = _tabs.Count > 0 ? _tabs[0].Id : string.Empty;

        RebuildTabs();
        ShowActiveTab();
        return Task.CompletedTask;
    }
}
