using System.Collections.ObjectModel;
using System.Windows.Input;

using Lertaro.App.Helpers;
using Lertaro.Core;
namespace Lertaro.App.ViewModels.Settings.QuickPanel;

/// <summary>
/// Backs the Quick Panel settings page: the workspace tabs down the left, the selected one's sources on
/// the right. Edits stage on these view models and only reach <see cref="UserSettings"/> when
/// <see cref="Save"/> runs, the same way every other settings page works.
/// </summary>
/// <remarks>
/// The tabs edited here are the panel's workspaces -- one set of sources per project -- which is not
/// what a tab means on the Plugin tabs page beside it, where one tab is one plugin's whole list. Same
/// word, two concepts; they must not be wired together.
/// </remarks>
public class QuickPanelSettingsViewModel : ViewModelBase
{
    private readonly UserSettings _userSettings;
    private readonly List<QuickPanelTab> _models;

    public QuickPanelSettingsViewModel(UserSettings userSettings)
    {
        _userSettings = userSettings;
        var panel = userSettings.QuickPanel;
        _enabled = panel.Enabled;
        _blacklistText = string.Join(Environment.NewLine, panel.BlacklistedProcesses);

        SelectSubTabCommand = new RelayCommand<string>(tab => SelectedSubTab = tab ?? "Sources");
        SelectSectionCommand = new RelayCommand<string>(section => SelectedSection = section ?? "Workspaces");
        AddTabCommand = new RelayCommand(AddTab);
        DuplicateTabCommand = new RelayCommand(DuplicateTab, () => SelectedTab != null);
        RemoveTabCommand = new RelayCommand(RemoveTab, () => SelectedTab != null);
        MoveTabUpCommand = new RelayCommand(() => MoveTab(-1), () => CanMoveTab(-1));
        MoveTabDownCommand = new RelayCommand(() => MoveTab(+1), () => CanMoveTab(+1));

        // Row-level commands, so each workspace carries its own reorder/delete buttons rather than a
        // toolbar under the list acting on the selection -- same shape as the plugin array editor, and
        // it takes the button strip out of a pane too narrow to hold five of them. Built before any row
        // exists, so every row can be handed them as it is created.
        _rowMoveUp = new RelayCommand<QuickPanelTabSettingsViewModel>(tab => MoveTab(tab, -1));
        _rowMoveDown = new RelayCommand<QuickPanelTabSettingsViewModel>(tab => MoveTab(tab, +1));
        _rowRemove = new RelayCommand<QuickPanelTabSettingsViewModel>(RemoveTab);

        // Cloned, not referenced: these objects live inside the process-wide UserSettings, so editing
        // them in place would make every change instantly live and survive Cancel. Save() puts the
        // working copies back, which is what makes this page stage like every other one.
        //
        // No tabs is a state the user can reach by deleting the last one, so it is loaded as it is
        // rather than quietly refilled with a default they would have to delete again.
        _models = panel.Tabs.Select(tab => tab.Clone()).ToList();
        foreach (var model in _models)
            Tabs.Add(BindRow(new QuickPanelTabSettingsViewModel(model)));

        _selectedTab = Tabs.FirstOrDefault(t => t.Id == panel.ActiveTabId) ?? Tabs.FirstOrDefault();

        PluginTabs = QuickPanelPluginTabCatalog.Available(panel);
    }

    private readonly ICommand _rowMoveUp;
    private readonly ICommand _rowMoveDown;
    private readonly ICommand _rowRemove;

    private QuickPanelTabSettingsViewModel BindRow(QuickPanelTabSettingsViewModel tab)
    {
        tab.BindRowCommands(_rowMoveUp, _rowMoveDown, _rowRemove);
        return tab;
    }

    private bool _enabled;

    /// <summary>Master switch: off and the hotkey does nothing at all.</summary>
    public bool Enabled
    {
        get => _enabled;
        set => SetProperty(ref _enabled, value);
    }

    private string _selectedSubTab = "Sources";

    /// <summary>
    /// Which half of the selected workspace is on screen: its sources, or the apps it belongs to. The
    /// process list is a few lines typed once and then forgotten, so it earns a tab of its own rather
    /// than sitting under the source list pushing it up the page.
    /// </summary>
    public string SelectedSubTab
    {
        get => _selectedSubTab;
        set => SetProperty(ref _selectedSubTab, value);
    }

    public ICommand SelectSubTabCommand { get; }

    private string _selectedSection = "Workspaces";

    /// <summary>Which half of the page is on screen: the workspaces, or the plugins' own tabs.</summary>
    /// <remarks>
    /// A tab row rather than one section under the other. Both are lists that grow, and stacked they
    /// pushed each other off the page -- the same reason the workspace pane below splits its own two
    /// halves the same way.
    /// </remarks>
    public string SelectedSection
    {
        get => _selectedSection;
        set => SetProperty(ref _selectedSection, value);
    }

    public ICommand SelectSectionCommand { get; }

    private string _blacklistText;

    /// <summary>
    /// Apps the panel stays out of, one process name per line. Added to the global hotkey blacklist
    /// rather than replacing it -- anything blocked there is blocked here too, and this list is for the
    /// apps only a panel that docks itself over the window in front has a reason to avoid.
    /// </summary>
    public string BlacklistText
    {
        get => _blacklistText;
        set => SetProperty(ref _blacklistText, value);
    }

    public ObservableCollection<QuickPanelTabSettingsViewModel> Tabs { get; } = new();

    private QuickPanelTabSettingsViewModel? _selectedTab;
    public QuickPanelTabSettingsViewModel? SelectedTab
    {
        get => _selectedTab;
        set
        {
            if (SetProperty(ref _selectedTab, value))
                CommandManager.InvalidateRequerySuggested();
        }
    }

    /// <summary>The tab strip only earns its space once there is more than one workspace.</summary>
    public bool HasMultipleTabs => Tabs.Count > 1;

    public ICommand AddTabCommand { get; }
    public ICommand DuplicateTabCommand { get; }
    public ICommand RemoveTabCommand { get; }
    public ICommand MoveTabUpCommand { get; }
    public ICommand MoveTabDownCommand { get; }

    public void Save()
    {
        foreach (var tab in Tabs)
            tab.Save();

        var panel = _userSettings.QuickPanel;
        panel.Enabled = Enabled;
        // BlacklistedProcesses is not written here: it is edited on the Hotkeys page, beside the global
        // list it adds to, and saved by BlacklistSettingsViewModel. Writing it from both would make
        // whichever page saved last the winner.
        // Ordered by the strip, not by _models: the list on screen is what the user arranged.
        panel.Tabs = Tabs.Select(t => _models.First(m => m.Id == t.Id)).ToList();
        panel.ActiveTabId = SelectedTab?.Id ?? panel.Tabs.FirstOrDefault()?.Id ?? string.Empty;

        // Only the ones unticked here, and only among the tabs that exist right now: a plugin currently
        // switched off has no row on this page, and rewriting the list from these rows alone would quietly
        // reopen a tab the user closed before disabling its plugin.
        var listed = PluginTabs.Select(option => option.Id).ToList();
        panel.ClosedPluginTabIds = Merge(panel.ClosedPluginTabIds, listed, option => !option.IsOpen);
        panel.ListViewPluginTabIds = Merge(panel.ListViewPluginTabIds, listed, option => option.ShowAsList);
    }

    /// <summary>Rewrites one of the per-plugin-tab lists from the rows, keeping what has no row.</summary>
    private List<string> Merge(List<string> stored, List<string> listed, Func<QuickPanelPluginTabOption, bool> isSet)
        => stored
            .Where(id => !listed.Contains(id, StringComparer.OrdinalIgnoreCase))
            .Concat(PluginTabs.Where(isSet).Select(option => option.Id))
            .ToList();

    /// <summary>The tabs the installed plugins offer, each ticked while the strip shows it.</summary>
    /// <remarks>
    /// A page-level list rather than something inside a workspace: a plugin's list is a whole collection,
    /// orthogonal to whichever folders a workspace gathers, so it is a tab beside them rather than a
    /// group inside one. It briefly was the other way, and the settings said so -- Favorites had to be
    /// ticked into every workspace one at a time, and was missing from every new one.
    /// </remarks>
    public IReadOnlyList<QuickPanelPluginTabOption> PluginTabs { get; }

    public bool HasPluginTabs => PluginTabs.Count > 0;

    /// <summary>Re-reads every label on this page after a language switch.</summary>
    /// <remarks>
    /// Driven from SettingsViewModel's own TranslationManager subscription rather than a second one here,
    /// so there is one handler to unhook in Cleanup() instead of one per page.
    ///
    /// Walked live rather than pushed to a list captured when the page was built: workspaces and sources
    /// are added and removed while the window is open, and a row created after the switch is already
    /// correct while one created before it is not.
    /// </remarks>
    public void NotifyLanguageChanged()
    {
        foreach (var tab in Tabs)
        {
            tab.NotifyLanguageChanged();
            foreach (var source in tab.Sources)
                source.RefreshTranslations();
        }

        foreach (var option in PluginTabs)
            option.NotifyLanguageChanged();
    }

    private void AddTab()
    {
        var model = new QuickPanelTab { Id = QuickPanelTab.NewId() };
        _models.Add(model);
        var tab = BindRow(new QuickPanelTabSettingsViewModel(model));
        Tabs.Add(tab);
        SelectedTab = tab;
        OnPropertyChanged(nameof(HasMultipleTabs));
    }

    // Copies the sources, not the panel's own per-group state: a workspace forked from another starts
    // with the same folders but is free to sort and collapse them differently.
    private void DuplicateTab()
    {
        if (SelectedTab == null)
            return;
        var source = _models.First(m => m.Id == SelectedTab.Id);
        SelectedTab.Save();

        var copy = new QuickPanelTab
        {
            Id = QuickPanelTab.NewId(),
            Name = SelectedTab.EffectiveName,
            DisabledGroupIds = source.DisabledGroupIds.ToList(),
        };
        var idMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in source.Folders)
        {
            var clone = QuickPanelFolderSource.For(folder.Path, folder.Kind);
            clone.Recursive = folder.Recursive;
            clone.FilterPattern = folder.FilterPattern;
            clone.MaxItems = folder.MaxItems;
            clone.MaxAgeMinutes = folder.MaxAgeMinutes;
            idMap[folder.Id] = clone.Id;
            copy.Folders.Add(clone);
        }
        // Order and hidden-list entries point at ids, which the clones do not share.
        copy.GroupOrder = source.GroupOrder.Select(id => idMap.TryGetValue(id, out var mapped) ? mapped : id).ToList();
        copy.DisabledGroupIds = copy.DisabledGroupIds.Select(id => idMap.TryGetValue(id, out var mapped) ? mapped : id).ToList();
        foreach (var (id, preference) in source.GroupPreferences)
        {
            copy.GroupPreferences[idMap.TryGetValue(id, out var mapped) ? mapped : id] = new QuickPanelGroupPreference
            {
                DisplayName = preference.DisplayName,
                Sort = preference.Sort,
                ThumbnailView = preference.ThumbnailView,
                Expanded = preference.Expanded,
            };
        }

        _models.Add(copy);
        var tab = BindRow(new QuickPanelTabSettingsViewModel(copy));
        Tabs.Add(tab);
        SelectedTab = tab;
        OnPropertyChanged(nameof(HasMultipleTabs));
    }

    private void RemoveTab() => RemoveTab(SelectedTab);

    // The last workspace goes too: an empty list is a legitimate state (no panel content configured),
    // and refusing to delete the final row to avoid it would be the page deciding for the user.
    private void RemoveTab(QuickPanelTabSettingsViewModel? tab)
    {
        if (tab == null)
            return;
        var index = Tabs.IndexOf(tab);
        _models.RemoveAll(m => m.Id == tab.Id);
        Tabs.Remove(tab);
        SelectedTab = Tabs.Count > 0 ? Tabs[Math.Min(index, Tabs.Count - 1)] : null;
        OnPropertyChanged(nameof(HasMultipleTabs));
    }

    private bool CanMoveTab(int delta)
    {
        if (SelectedTab == null)
            return false;
        var to = Tabs.IndexOf(SelectedTab) + delta;
        return to >= 0 && to < Tabs.Count;
    }

    private void MoveTab(int delta) => MoveTab(SelectedTab, delta);

    private void MoveTab(QuickPanelTabSettingsViewModel? tab, int delta)
    {
        if (tab == null)
            return;
        var from = Tabs.IndexOf(tab);
        var to = from + delta;
        if (from < 0 || to < 0 || to >= Tabs.Count)
            return;
        Tabs.Move(from, to);
    }
}
