using System.Collections.ObjectModel;
using System.Windows.Input;
using Lertaro.App.Helpers;
using Lertaro.App.Services;
using Lertaro.App.ViewModels.Search;
using Lertaro.Core;
using Lertaro.Core.SearchIndex;
using Lertaro.PluginSdk.Abstractions.Plugins;

namespace Lertaro.App.ViewModels.Settings.Plugins;

/// <summary>
/// ViewModel for the Plugin Management settings page.
/// Loads installed plugins and exposes their sub-components with enable/disable toggles.
/// </summary>
public class PluginManagementViewModel : ViewModelBase
{
    private readonly System.ComponentModel.PropertyChangedEventHandler _translationHandler;

    private readonly UserSettings _userSettings;

    public PluginManagementViewModel(UserSettings userSettings)
    {
        _userSettings = userSettings;
        Plugins = new ObservableCollection<PluginInfoViewModel>(PluginLoaderHelper.BuildPluginList(_userSettings));
        ShowPluginManagementCommand = new RelayCommand(() => IsRuntimeStatusTab = false);
        ShowRuntimeStatusCommand = new RelayCommand(() => IsRuntimeStatusTab = true);
        // The default sort is "enabled" (disabled sink to the bottom), so reconcile the freshly built
        // list to it once here -- BuildPluginList only returns rank-then-name order. Selecting happens
        // AFTER the sort, so the initial selection lands on the first enabled plugin, not whatever
        // happened to sort first by name.
        ApplyPluginSort();
        _selectedPlugin = Plugins.FirstOrDefault();

        // Dynamically refresh the plugin list when language changes to dynamically apply localized plugin names
        _translationHandler = (s, e) =>
        {
            // Keeping the selection across a rebuild means matching on the one stable identifier a
            // rebuilt view model shares with the old one -- the instances themselves are all new.
            var selectedDll = SelectedPlugin?.DllFileName;
            var newList = PluginLoaderHelper.BuildPluginList(_userSettings);
            Plugins.Clear();
            foreach (var p in newList)
                Plugins.Add(p);
            // Rebuild only if the runtime tab has actually been opened (see EnsureRuntimeStatusesBuilt);
            // otherwise the next time it is shown rebuilds from the new list anyway.
            if (_runtimeStatusesBuilt) RebuildRuntimeStatuses();
            ApplyPluginSort();
            SelectedPlugin = Plugins.FirstOrDefault(p => p.DllFileName == selectedDll) ?? Plugins.FirstOrDefault();
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(DevGuideUri));
        };
        TranslationManager.Instance.PropertyChanged += _translationHandler;

    }

    public ObservableCollection<PluginInfoViewModel> Plugins { get; }
    private readonly List<PluginRuntimeStatusItemViewModel> _allRuntimeStatuses = new();
    public ObservableCollection<PluginRuntimeStatusItemViewModel> RuntimeStatuses { get; } = new();

    private string _runtimeStatusSearchText = string.Empty;
    public string RuntimeStatusSearchText
    {
        get => _runtimeStatusSearchText;
        set
        {
            if (!SetProperty(ref _runtimeStatusSearchText, value)) return;
            EnsureRuntimeStatusesBuilt();
            ApplyRuntimeStatusFilterAndSort();
        }
    }

    private void RebuildRuntimeStatuses()
    {
        _allRuntimeStatuses.Clear();
        _allRuntimeStatuses.AddRange(Plugins.Select(static p => new PluginRuntimeStatusItemViewModel(p)));
        _runtimeStatusesBuilt = true;
        ApplyRuntimeStatusFilterAndSort();
    }

    // The tab is hidden by default and building a VM per plugin (each reading a performance snapshot)
    // was pure cost for a page the user may never open, so it is deferred to the first actual display.
    private bool _runtimeStatusesBuilt;

    /// <summary>Builds the runtime-status rows if they have not been built yet.</summary>
    public void EnsureRuntimeStatusesBuilt()
    {
        if (!_runtimeStatusesBuilt) RebuildRuntimeStatuses();
    }

    internal void SortRuntimeStatuses(string column)
    {
        (RuntimeStatusSortColumn, RuntimeStatusSortDescending) =
            SearchResultSortCycle.Advance(RuntimeStatusSortColumn, RuntimeStatusSortDescending, column);

        ApplyRuntimeStatusFilterAndSort();
    }

    private void ApplyRuntimeStatusFilterAndSort()
    {
        var query = RuntimeStatusSearchText.Trim();
        var filtered = _allRuntimeStatuses.Where(status =>
            string.IsNullOrEmpty(query) || FuzzyMatcher.IsMatch(query, status.Name));
        filtered = RuntimeStatusSortColumn switch
        {
            nameof(PluginRuntimeStatusItemViewModel.InvocationCount) => RuntimeStatusSortDescending
                ? filtered.OrderByDescending(status => status.InvocationCount)
                : filtered.OrderBy(status => status.InvocationCount),
            nameof(PluginRuntimeStatusItemViewModel.AverageElapsedMilliseconds) => RuntimeStatusSortDescending
                ? filtered.OrderByDescending(status => status.AverageElapsedMilliseconds)
                : filtered.OrderBy(status => status.AverageElapsedMilliseconds),
            nameof(PluginRuntimeStatusItemViewModel.LastElapsedMilliseconds) => RuntimeStatusSortDescending
                ? filtered.OrderByDescending(status => status.LastElapsedMilliseconds)
                : filtered.OrderBy(status => status.LastElapsedMilliseconds),
            nameof(PluginRuntimeStatusItemViewModel.MaxElapsedMilliseconds) => RuntimeStatusSortDescending
                ? filtered.OrderByDescending(status => status.MaxElapsedMilliseconds)
                : filtered.OrderBy(status => status.MaxElapsedMilliseconds),
            nameof(PluginRuntimeStatusItemViewModel.AllocatedMegabytes) => RuntimeStatusSortDescending
                ? filtered.OrderByDescending(status => status.AllocatedMegabytes)
                : filtered.OrderBy(status => status.AllocatedMegabytes),
            nameof(PluginRuntimeStatusItemViewModel.ExceptionCount) => RuntimeStatusSortDescending
                ? filtered.OrderByDescending(status => status.ExceptionCount)
                : filtered.OrderBy(status => status.ExceptionCount),
            _ => filtered
        };
        SyncRuntimeStatusCollection(RuntimeStatuses, filtered.ToList());
    }

    internal static void SyncRuntimeStatusCollection(
        ObservableCollection<PluginRuntimeStatusItemViewModel> current,
        IReadOnlyList<PluginRuntimeStatusItemViewModel> desired)
    {
        for (var i = current.Count - 1; i >= 0; i--)
        {
            if (!desired.Contains(current[i]))
                current.RemoveAt(i);
        }

        for (var i = 0; i < desired.Count; i++)
        {
            if (i < current.Count && ReferenceEquals(current[i], desired[i]))
                continue;

            var currentIndex = current.IndexOf(desired[i]);
            if (currentIndex >= 0)
                current.Move(currentIndex, i);
            else
                current.Insert(i, desired[i]);
        }
    }

    public string RuntimeStatusSortColumn { get; private set; } = string.Empty;
    public bool RuntimeStatusSortDescending { get; private set; } = true;

    private bool _isRuntimeStatusTab;
    public bool IsRuntimeStatusTab
    {
        get => _isRuntimeStatusTab;
        set
        {
            if (!SetProperty(ref _isRuntimeStatusTab, value)) return;
            // Build on first display, not at construction -- see EnsureRuntimeStatusesBuilt.
            if (value) EnsureRuntimeStatusesBuilt();
            OnPropertyChanged(nameof(IsPluginManagementTab));
        }
    }

    public bool IsPluginManagementTab => !IsRuntimeStatusTab;
    public ICommand ShowPluginManagementCommand { get; }
    public ICommand ShowRuntimeStatusCommand { get; }

    public void RefreshRuntimeStatus()
    {
        EnsureRuntimeStatusesBuilt();
        foreach (var status in _allRuntimeStatuses)
            status.Refresh();
        ApplyRuntimeStatusFilterAndSort();
    }

    // Single sortable column: it toggles between the default rank order and "disabled sink to the
    // bottom". The header text follows the mode. TogglePluginSort is the header's click handler;
    // ApplyPluginSort reorders Plugins in place with Move so the selected row keeps its VM and stays
    // selected. Defaults to "enabled" (disabled sink to the bottom), matching the upstream ordering.
    private bool _disabledLast = true;

    /// <summary>The column header text for the current sort rule: "name" while in the default rank
    /// order, "enabled" while disabled plugins are sunk to the bottom.</summary>
    public string PluginSortLabel => TranslationManager.Instance[_disabledLast ? "Plugins_ColumnEnabled" : "Plugins_ColumnName"];

    /// <summary>Raised after the list is reordered, so the view can scroll the selection back into view.</summary>
    public event Action? PluginsReordered;

    public void TogglePluginSort()
    {
        _disabledLast = !_disabledLast;
        OnPropertyChanged(nameof(PluginSortLabel));
        ApplyPluginSort();
    }

    /// <summary>Pure ordering for the plugin list: default rank order, or rank with disabled sunk last.</summary>
    internal static List<PluginInfoViewModel> SortPluginsList(
        IReadOnlyList<PluginInfoViewModel> plugins, bool disabledLast)
    {
        // Disabled plugins sink below every active one (IsFullyDisabled false < true); within each side
        // the rank bands then name still apply. In the default order there is no disabled split.
        var ordered = disabledLast
            ? plugins.OrderBy(p => p.IsFullyDisabled)
            : (IOrderedEnumerable<PluginInfoViewModel>)plugins.OrderBy(_ => 0);

        return ordered
            .ThenBy(p => PluginLoaderHelper.DisplayRank(p.HasConfigFields, p.RawComponents.Any(c => c.IsToggleable)))
            .ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private void ApplyPluginSort()
    {
        var sorted = SortPluginsList(Plugins, _disabledLast);
        for (var i = 0; i < sorted.Count; i++)
        {
            var current = Plugins.IndexOf(sorted[i]);
            if (current != i)
                Plugins.Move(current, i);
        }

        PluginsReordered?.Invoke();
    }

    private PluginInfoViewModel? _selectedPlugin;

    /// <summary>
    /// The plugin whose details the right-hand pane shows.
    /// </summary>
    /// <remarks>
    /// Replaces the per-card expand/collapse this page used to have. With one plugin shown at a time
    /// there is nothing left for a card to expand INTO, so the concept went rather than being kept as a
    /// second, redundant way to say "this is the one I am looking at".
    ///
    /// Switching away rolls back any config the previous plugin had open and unsaved: the section is
    /// gone from view either way, and leaving edits staged in a view model nobody can see is how they
    /// end up written by a later OK that meant something else.
    /// </remarks>
    public PluginInfoViewModel? SelectedPlugin
    {
        get => _selectedPlugin;
        set
        {
            if (ReferenceEquals(_selectedPlugin, value)) return;

            // Setting this back rolls the config fields back with it (see IsConfigTab), so a plugin
            // left mid-edit does not keep those edits staged while out of view.
            if (_selectedPlugin != null)
            {
                _selectedPlugin.RollbackConfig();
                if (_selectedPlugin.IsConfigTab)
                    _selectedPlugin.IsConfigTab = false;
            }

            SetProperty(ref _selectedPlugin, value);
        }
    }

    public bool IsEmpty => Plugins.Count == 0;

    public string HostSdkVersion { get; } = typeof(IPlugin).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";

    // The dev guide link's target differs per locale (/zh-CN/ prefix), so it's derived from the
    // same translated URL string the link displays -- see AboutSettingsPage.UserGuideUri for the
    // same pattern applied to the user manual link.
    public Uri? DevGuideUri => Uri.TryCreate(TranslationManager.Instance["Plugins_DevGuideUrl"], UriKind.Absolute, out var uri) ? uri : null;

    public void Save()
    {
        // Apply only the components the user actually toggled on this page (IsDirty), merged into the
        // CURRENT disabled list rather than replacing it wholesale -- Plugins is a snapshot taken when
        // the Settings window opened, so a blind replace would silently revert any component disabled
        // or re-enabled through another channel since then (e.g. a Startup Panel tab's x button, or the
        // Startup Panel settings page's own re-enable checkbox).
        var disabled = new HashSet<string>(_userSettings.DisabledPluginComponents, StringComparer.OrdinalIgnoreCase);

        foreach (var c in Plugins.SelectMany(p => p.RawComponents).Where(c => c.IsToggleable && c.IsDirty))
        {
            if (c.IsEnabled)
                disabled.Remove(c.ComponentId);
            else
                disabled.Add(c.ComponentId);
        }

        _userSettings.DisabledPluginComponents = disabled.ToList();

        // An open config the user edited is written here too, so the Settings window's Apply/OK is the
        // single persistence route for plugin configuration.
        PluginConfigCommitSupport.Commit(PluginConfigCommitSupport.PendingOnSettingsApply(SelectedPlugin));

        // Toggling a component deliberately does not reorder the list live (that read as jumping); the
        // position is reconciled here, on Apply/OK, against the currently selected sort rule.
        ApplyPluginSort();
    }

    public void Cleanup() => TranslationManager.Instance.PropertyChanged -= _translationHandler;
}
