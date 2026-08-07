using System.Collections.ObjectModel;
using Lertaro.Core;
using Lertaro.App.Services.QuickPanel;
using Lertaro.App.ViewModels.QuickPanel.Loading;

namespace Lertaro.App.ViewModels.QuickPanel;

// Backs the quick panel: one workspace at a time, its sources shown as groups, over whatever window is
// in front.
//
// Everything it shows comes from QuickPanelSettings -- which workspaces exist, which of their sources
// are visible and in what order, and what each group is called. Loading a source's entries is
// QuickPanelSourceLoader's job, and both it and the settings arrive through the constructor so the
// assembly logic here can be tested without an index behind it.
//
// See QuickPanelViewModelLoading for what a refresh actually loads, QuickPanelViewModelFilter for the
// box at the top, and QuickPanelViewModelWriteback for the two things the strip writes back.
public partial class QuickPanelViewModel : ViewModelBase
{
    private readonly Func<QuickPanelSettings> _readSettings;
    private readonly Func<QuickPanelFolderSource, CancellationToken, Task<List<SearchResult>>> _load;
    private readonly Func<QuickPanelFolderSource, IProgress<IReadOnlyList<SearchResult>>, CancellationToken, Task<List<SearchResult>>> _loadProgressively;
    private readonly Action _saveSettings;
    private readonly QuickPanelGroupLoader _groupLoader;

    /// <summary>The tabs on screen: enabled, and with something behind them.</summary>
    /// <remarks>
    /// Workspaces and plugin tabs together, in one order. Which kind a tab is matters only while it
    /// loads and when it is closed, both of which the source answers for itself.
    /// </remarks>
    private List<IQuickPanelTabSource> _tabs = new();

    /// <summary>What each of those loaded, so switching between them is a swap rather than a reload.</summary>
    private Dictionary<string, List<QuickPanelGroupViewModel>> _content = new(StringComparer.OrdinalIgnoreCase);

    private string _activeTabId = string.Empty;

    public QuickPanelViewModel(
        Func<QuickPanelSettings>? readSettings = null,
        Func<QuickPanelFolderSource, CancellationToken, Task<List<SearchResult>>>? load = null,
        Action? saveSettings = null)
    {
        _readSettings = readSettings ?? (() => UserSettings.Load().QuickPanel);
        _load = load ?? QuickPanelSourceLoader.LoadAsync;
        _loadProgressively = load == null
            ? QuickPanelSourceLoader.LoadProgressivelyAsync
            : async (source, _, token) => await _load(source, token).ConfigureAwait(true);
        _saveSettings = saveSettings ?? (() => UserSettings.Load().Save());
        _groupLoader = new QuickPanelGroupLoader(_loadProgressively, mapOnBackground: load == null);

        // Dragging a tab reorders this collection in place (a remove and an insert, which is what
        // DragReorder does to any IList), so the strip is where a new order first exists and this is the
        // only place that can notice it.
        Tabs.CollectionChanged += (_, _) => PersistTabOrder();
    }

    /// <summary>The workspace strip. Empty until the first refresh, and rebuilt by every one after.</summary>
    public ObservableCollection<QuickPanelTabViewModel> Tabs { get; } = new();

    /// <summary>One entry per visible source of the workspace on screen, each in its own order.</summary>
    public ObservableCollection<QuickPanelGroupViewModel> Groups { get; } = new();

    /// <summary>The widest line number any group currently on screen needs.</summary>
    /// <remarks>Every group binds its panel-owned gutter here, so their rows start on one vertical line.</remarks>
    public int LargestVisibleGroupItemCount => Groups.Where(group => group.HasMatches)
        .Select(group => group.Count).DefaultIfEmpty().Max();

    private void UpdateLineNumberSizing() => OnPropertyChanged(nameof(LargestVisibleGroupItemCount));

    /// <summary>Whether there is a strip to draw at all, which there is for even one workspace.</summary>
    /// <remarks>
    /// It used to take two, on the argument that a strip of one names the only thing there is. That was
    /// wrong in practice: the tab is also where the workspace's name is, and where its close button is,
    /// and a panel that showed neither until a second workspace existed left the first one unnamed and
    /// unclosable. It shares a row with the filter box, so it costs no height either way.
    /// </remarks>
    public bool HasTabStrip => Tabs.Count > 0;

    /// <summary>Whether there is anything worth opening the panel for.</summary>
    /// <remarks>
    /// Distinct from IsEmpty below, which they are easy to mistake for each other. This one gates
    /// whether the panel opens at all; that one is about the tab you are looking at once it has.
    /// </remarks>
    public bool HasContent => Groups.Count > 0 || Tabs.Count > 1;

    private bool _isEmpty = true;

    /// <summary>Whether the tab on screen has nothing in it, which the panel says rather than implies.</summary>
    public bool IsEmpty
    {
        get => _isEmpty;
        private set => SetProperty(ref _isEmpty, value);
    }

    /// <summary>
    /// Which workspace to open on: the one the app in front claims, else wherever the user left the
    /// panel, else the one the settings recorded, else the first there is.
    /// </summary>
    /// <remarks>
    /// The app in front outranks the remembered tab deliberately -- a workspace that names an app is a
    /// statement that this app means these folders, and honouring where the panel was last left instead
    /// would make that rule fire only when it happened to agree.
    ///
    /// Answered against every enabled workspace rather than the ones that turned out to have content,
    /// because nothing has loaded yet when this runs: the answer is what the panel WANTS to open on, and
    /// AddWorkspaceTab settles for something else only until that one arrives.
    /// </remarks>
    /// <remarks>
    /// Only a workspace can be claimed by the app in front: claiming is a statement that this app means
    /// these folders, and a plugin's list is not anyone's folders. Everything after that is answered over
    /// the whole strip, plugin tabs included -- they can be the last tab you were on, and they can be the
    /// only tab there is.
    /// </remarks>
    private string ResolveActiveTabId(
        QuickPanelSettings settings, string? processName, List<QuickPanelTab> workspaces, List<IQuickPanelTabSource> candidates)
    {
        var claimed = QuickPanelTabSelection.SelectTabId(processName, workspaces);
        if (claimed != null)
            return claimed;
        if (Contains(_activeTabId))
            return _activeTabId;
        if (Contains(settings.ActiveTabId))
            return settings.ActiveTabId;
        return candidates.Count > 0 ? candidates[0].Id : string.Empty;

        bool Contains(string id) => !string.IsNullOrEmpty(id)
            && candidates.Any(tab => tab.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
    }

    private bool _rebuildingTabs;

    private void RebuildTabs()
    {
        // Rebuilding empties and refills the strip one item at a time, and every one of those steps is a
        // collection change like any other. Without this the first Clear would persist an empty order.
        _rebuildingTabs = true;
        try
        {
            Tabs.Clear();
            foreach (var tab in _tabs)
            {
                // Captured by value: the tab list is replaced wholesale on every refresh, so a command
                // that closed over the source object would keep the old one alive and act on a stale copy.
                var id = tab.Id;
                Tabs.Add(new QuickPanelTabViewModel(
                    id, tab.Name, () => _ = SelectTabAsync(id), () => _ = CloseTabAsync(id))
                {
                    IsSelected = id == _activeTabId,
                });
            }
        }
        finally
        {
            _rebuildingTabs = false;
        }
        OnPropertyChanged(nameof(HasTabStrip));
    }

    /// <summary>Switches the panel to another tab. Everything is already loaded, so this is a swap.</summary>
    public Task SelectTabAsync(string tabId, CancellationToken token = default)
    {
        if (string.IsNullOrEmpty(tabId) || tabId == _activeTabId)
            return Task.CompletedTask;
        if (!_tabs.Any(tab => tab.Id == tabId))
            return Task.CompletedTask;

        _activeTabId = tabId;
        foreach (var tab in Tabs)
            tab.IsSelected = tab.Id == tabId;

        ShowActiveTab();
        return Task.CompletedTask;
    }

    /// <summary>Switches to the nth tab in the strip, for the number-key shortcut. 1-based.</summary>
    public Task SelectTabAtAsync(int oneBasedIndex, CancellationToken token = default)
        => oneBasedIndex >= 1 && oneBasedIndex <= Tabs.Count
            ? SelectTabAsync(Tabs[oneBasedIndex - 1].Id, token)
            : Task.CompletedTask;

    /// <summary>Puts the active tab's groups on screen, keeping whatever is typed in the box.</summary>
    private void ShowActiveTab()
    {
        Groups.Clear();
        if (_content.TryGetValue(_activeTabId, out var groups))
        {
            foreach (var group in groups)
                Groups.Add(group);
        }

        // Switching workspace with something typed keeps the filter: the box is still on screen with
        // that text in it, and a strip that quietly showed everything again would be contradicting it.
        if (SearchQuery.Length > 0)
            ApplyFilter();
        else
            IsEmpty = Groups.Count == 0;

        OnPropertyChanged(nameof(HasContent));
        UpdateLineNumberSizing();

        RewatchIfWatching();
    }
}
