using System.Collections.ObjectModel;
using System.Windows.Input;
using Lertaro.App.Helpers;
using Lertaro.App.Services;
using Lertaro.App.ViewModels.Search;
using Lertaro.Core;
using Lertaro.Core.SearchIndex;
using Lertaro.Core.Wire;
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
        RebuildRuntimeStatuses();
        SaveConfigCommand = new RelayCommand<PluginInfoViewModel>(SaveConfig);
        ShowPluginManagementCommand = new RelayCommand(() => IsRuntimeStatusTab = false);
        ShowRuntimeStatusCommand = new RelayCommand(() => IsRuntimeStatusTab = true);
        _selectedPlugin = Plugins.FirstOrDefault();
        AttachFullyDisabledWatch();

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
            RebuildRuntimeStatuses();
            SelectedPlugin = Plugins.FirstOrDefault(p => p.DllFileName == selectedDll) ?? Plugins.FirstOrDefault();
            AttachFullyDisabledWatch();
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
            if (SetProperty(ref _runtimeStatusSearchText, value))
                RebuildRuntimeStatuses();
        }
    }

    private void RebuildRuntimeStatuses()
    {
        _allRuntimeStatuses.Clear();
        _allRuntimeStatuses.AddRange(Plugins.Select(static p => new PluginRuntimeStatusItemViewModel(p)));
        ApplyRuntimeStatusFilterAndSort();
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
            if (SetProperty(ref _isRuntimeStatusTab, value))
                OnPropertyChanged(nameof(IsPluginManagementTab));
        }
    }

    public bool IsPluginManagementTab => !IsRuntimeStatusTab;
    public ICommand ShowPluginManagementCommand { get; }
    public ICommand ShowRuntimeStatusCommand { get; }

    public void RefreshRuntimeStatus()
    {
        foreach (var status in _allRuntimeStatuses)
            status.Refresh();
        ApplyRuntimeStatusFilterAndSort();
    }

    // Toggling a plugin's last components off (or back on) moves its card to its sorted position
    // in place, instead of waiting for the next rebuild.
    private void AttachFullyDisabledWatch()
    {
        foreach (var plugin in Plugins)
            plugin.FullyDisabledChanged += p => MovePluginForDisabledState(Plugins, p);
    }

    internal static void MovePluginForDisabledState(ObservableCollection<PluginInfoViewModel> plugins, PluginInfoViewModel plugin)
    {
        var currentIndex = plugins.IndexOf(plugin);
        if (currentIndex < 0) return;

        // Remove first, then re-insert at the first position that sorts after the moved plugin:
        // that is exactly where SortForDisplay would have put it, in the disabled tail as well
        // as back among the active band. No position found means it belongs at the end.
        plugins.RemoveAt(currentIndex);

        var insertAt = plugins.Count;
        for (var i = 0; i < plugins.Count; i++)
        {
            if (CompareDisplayOrder(plugins[i], plugin) > 0)
            {
                insertAt = i;
                break;
            }
        }

        plugins.Insert(insertAt, plugin);
    }

    private static int CompareDisplayOrder(PluginInfoViewModel left, PluginInfoViewModel right)
    {
        var byDisabled = left.IsFullyDisabled.CompareTo(right.IsFullyDisabled);
        if (byDisabled != 0) return byDisabled;

        var byRank = PluginLoaderHelper.DisplayRank(left.HasConfigFields, left.RawComponents.Any(c => c.IsToggleable))
            .CompareTo(PluginLoaderHelper.DisplayRank(right.HasConfigFields, right.RawComponents.Any(c => c.IsToggleable)));
        if (byRank != 0) return byRank;

        return string.Compare(left.Name, right.Name, StringComparison.CurrentCultureIgnoreCase);
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

    /// <summary>The config tab's OK button: writes the fields the user edited.</summary>
    public ICommand SaveConfigCommand { get; }

    // Same three steps the modal's OK did: commit every field, persist once through the settings object
    // they share, and tell the hook process to re-read what just changed.
    private void SaveConfig(PluginInfoViewModel? plugin)
    {
        if (plugin == null || plugin.ConfigFields.Count == 0) return;

        foreach (var field in plugin.ConfigFields)
            field.Commit();

        plugin.ConfigFields[0].Settings.Save();
        plugin.OnSave?.Invoke();
        InlineSearchManager.Instance.ExplorerTracker.RefreshActiveWindowAdapters();
        App.HookClient?.SendMessage(new IpcMessage { Id = IpcMessageId.ReloadSettings });
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
    }

    public void Cleanup() => TranslationManager.Instance.PropertyChanged -= _translationHandler;
}
