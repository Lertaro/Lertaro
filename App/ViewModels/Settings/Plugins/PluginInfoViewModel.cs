using System.Collections.ObjectModel;
using System.Windows.Input;
using Lertaro.App.Helpers;
using Lertaro.App.Services;

namespace Lertaro.App.ViewModels.Settings.Plugins;

/// <summary>
/// Represents the strongly-typed categories of plugin components.
/// </summary>
public enum PluginComponentType
{
    Action,
    DynamicActionProvider,
    InstantProvider,
    FullSearchFileResultProvider,
    SearchableItemProvider,
    FilterProvider,
    ColumnProvider,
    AliasProvider,
    ActivePathCollector,
    FileDialogAdapter,
    InlineSearchAdapter,
    FilePreviewProvider,
    QuickNavigationProvider,
    ThumbnailProvider,
    QueryTokenProvider,
    QuickPanelTabProvider,
    SearchScopeProvider,
    /// <summary>Translation providers are displayed read-only; they cannot be disabled.</summary>
    TranslationProvider,
    /// <summary>Theme providers are displayed read-only; they cannot be disabled.</summary>
    ThemeProvider
}

/// <summary>
/// Represents a group of plugin components of the same type.
/// </summary>
public class PluginComponentGroupViewModel : ViewModelBase
{
    public PluginComponentGroupViewModel(PluginComponentType componentType, List<PluginComponentViewModel> components)
    {
        ComponentType = componentType;
        Components = new ObservableCollection<PluginComponentViewModel>(components);
        ToggleAllCommand = new RelayCommand(ToggleAllComponents);

        // TranslationProvider/ThemeProvider components have no checkbox at all (see IsToggleable),
        // so there's nothing for a select-all button to toggle in those groups.
        foreach (var component in Components.Where(c => c.IsToggleable))
            component.PropertyChanged += OnComponentIsEnabledChanged;
    }

    public PluginComponentType ComponentType { get; }
    public string GroupName => TranslationManager.Instance[$"Plugins_Type{ComponentType}"];
    public ObservableCollection<PluginComponentViewModel> Components { get; }

    // A single toggleable component has nothing to "select all" -- its own checkbox already does that.
    public bool HasToggleableComponents => Components.Count(c => c.IsToggleable) > 1;
    public bool AreAllToggleableComponentsEnabled => Components.Where(c => c.IsToggleable).All(c => c.IsEnabled);
    public string SelectAllToggleLabel => TranslationManager.Instance[AreAllToggleableComponentsEnabled ? "Common_DeselectAll" : "Common_SelectAll"];

    public ICommand ToggleAllCommand { get; }

    private void OnComponentIsEnabledChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PluginComponentViewModel.IsEnabled))
            OnPropertyChanged(nameof(SelectAllToggleLabel));
    }

    private void ToggleAllComponents()
    {
        var setTo = !AreAllToggleableComponentsEnabled;
        foreach (var component in Components.Where(c => c.IsToggleable))
            component.IsEnabled = setTo;
    }
}

/// <summary>
/// Represents a loaded plugin with its name, version, source DLL, and grouped sub-components.
/// </summary>
public class PluginInfoViewModel : ViewModelBase
{
    public PluginInfoViewModel(
        string name,
        string version,
        string dllFileName,
        string sdkVersion,
        List<PluginComponentViewModel> components,
        List<PluginConfigFieldViewModel> configFields,
        string description = "",
        Action? onSave = null,
        Action? onRollback = null,
        string? websiteUrl = null,
        string? websiteLabel = null)
    {
        Name = name;
        Version = version;
        DllFileName = dllFileName;
        SdkVersion = sdkVersion;
        RawComponents = components;
        ConfigFields = new ObservableCollection<PluginConfigFieldViewModel>(configFields);
        _configTabState = new PluginConfigTabState(this);
        Description = description;
        OnSave = onSave;
        OnRollback = onRollback;
        WebsiteUrl = websiteUrl;
        WebsiteLabel = websiteLabel;
        ToggleAllComponentsCommand = new RelayCommand(ToggleAllComponents);

        // Group components by type
        var groups = components
            .GroupBy(c => c.ComponentType)
            .OrderBy(g => g.Key)
            .Select(g => new PluginComponentGroupViewModel(g.Key, g.ToList()))
            .ToList();

        ComponentGroups = new ObservableCollection<PluginComponentGroupViewModel>(groups);

        // Flat "group header, then its rows" list for the virtualized ListBox (only visible rows built).
        ComponentRows = groups
            .SelectMany(g => new object[] { g }.Concat(g.Components))
            .ToList();

        // TranslationProvider/ThemeProvider components have no checkbox at all (see IsToggleable),
        // so there's nothing for the plugin-wide select-all button to toggle for those.
        foreach (var component in RawComponents.Where(c => c.IsToggleable))
            component.PropertyChanged += OnComponentIsEnabledChanged;

        // Snapshot without raising the event: a plugin restored from persisted settings may
        // start out fully disabled, and the list is about to be sorted by that state anyway.
        _isFullyDisabled = ComputeFullyDisabled();
    }

    public string Name { get; }
    public string Description { get; }
    public string Version { get; }
    public string DllFileName { get; }
    public string SdkVersion { get; }
    public string? WebsiteUrl { get; }
    public string? WebsiteLabel { get; }
    public bool HasWebsite => !string.IsNullOrWhiteSpace(WebsiteUrl);
    public string DisplayWebsiteLabel => !string.IsNullOrWhiteSpace(WebsiteLabel)
        ? WebsiteLabel
        : TranslationManager.Instance["Plugins_VisitWebsite"];

    private ICommand? _openWebsiteCommand;
    public ICommand OpenWebsiteCommand => _openWebsiteCommand ??= new RelayCommand(() =>
    {
        if (!string.IsNullOrWhiteSpace(WebsiteUrl))
            UrlLauncher.Open(WebsiteUrl);
    });

    public List<PluginComponentViewModel> RawComponents { get; }
    public ObservableCollection<PluginComponentGroupViewModel> ComponentGroups { get; }
    public IReadOnlyList<object> ComponentRows { get; }
    public ObservableCollection<PluginConfigFieldViewModel> ConfigFields { get; }

    public bool HasConfigFields => ConfigFields.Count > 0;
    public bool HasNoComponents => RawComponents.Count == 0;

    /// <summary>
    /// Whether this plugin's config holds edits the user staged and has not had applied yet -- kept
    /// across plugin switches, since the Settings window's Apply/OK is the one commit point.
    /// </summary>
    public bool HasPendingConfigEdits => ConfigFields.Any(f => f.IsDirty);

    // Plugin-wide select-all/deselect-all, toggling every component across every group at once --
    // separate from each PluginComponentGroupViewModel's own per-group toggle. Same single-item
    // exception as the per-group button.
    public bool HasToggleableComponents => RawComponents.Count(c => c.IsToggleable) > 1;
    public bool AreAllToggleableComponentsEnabled => RawComponents.Where(c => c.IsToggleable).All(c => c.IsEnabled);
    public string SelectAllToggleLabel => TranslationManager.Instance[AreAllToggleableComponentsEnabled ? "Common_DeselectAll" : "Common_SelectAll"];

    public ICommand ToggleAllComponentsCommand { get; }

    private bool _isFullyDisabled;

    /// <summary>
    /// Whether every toggleable component of this plugin is currently disabled. A plugin with no
    /// toggleable components at all (translation/theme-only) can never be "fully disabled" --
    /// there is nothing the user turned off.
    /// </summary>
    public bool IsFullyDisabled
    {
        get => _isFullyDisabled; private set => SetProperty(ref _isFullyDisabled, value);
    }

    private bool ComputeFullyDisabled()
    {
        var toggleable = RawComponents.Where(c => c.IsToggleable).ToList();
        return toggleable.Count > 0 && toggleable.All(c => !c.IsEnabled);
    }

    private void OnComponentIsEnabledChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(PluginComponentViewModel.IsEnabled)) return;
        OnPropertyChanged(nameof(SelectAllToggleLabel));
        IsFullyDisabled = ComputeFullyDisabled();
    }

    private void ToggleAllComponents()
    {
        var setTo = !AreAllToggleableComponentsEnabled;
        foreach (var component in RawComponents.Where(c => c.IsToggleable))
            component.IsEnabled = setTo;
    }

    private bool _isConfigTab;
    private readonly PluginConfigTabState _configTabState;

    /// <summary>
    /// Which of the pane's two tabs is showing: false for the plugin's details, true for its config.
    /// Starts on details, so selecting a plugin shows what it provides rather than dropping into a form.
    /// Leaving the config tab no longer rolls its fields back: edits stay staged until the Settings
    /// window's Apply/OK writes them, so flipping to Details (or another plugin) and back must still show
    /// what the user typed. Cancel discards, via SettingsViewModel.Cleanup; the rows are still dropped
    /// cheaply on the way out and rebuilt on the next open (see PluginConfigTabState).
    /// </summary>
    public bool IsConfigTab
    {
        get => _isConfigTab;
        set
        {
            if (_isConfigTab == value) return;

            if (value) _configTabState.Opened();
            else _configTabState.Closed();
            SetProperty(ref _isConfigTab, value);
        }
    }

    public Action? OnSave { get; }
    public Action? OnRollback { get; }

    /// <summary>Discards every staged config edit on this plugin, returning its fields to persisted values.</summary>
    /// <remarks>Used by Cancel (SettingsViewModel.Cleanup) and by tests.</remarks>
    public void RollbackConfig()
    {
        if (!_configTabState.DiscardStagedEdits()) return;

        _selectedConfigGroup = ConfigGroups.FirstOrDefault();
        OnPropertyChanged(nameof(SelectedConfigGroup));
        OnPropertyChanged(nameof(ActiveConfigGroupChildren));
        OnPropertyChanged(nameof(FlatConfigFields));
        OnRollback?.Invoke();
    }

    /// <summary>
    /// Releases the rebuildable config rows of fields with no staged edit when this plugin stops being
    /// selected -- the selection path's replacement for the old rollback that lost the user's edits.
    /// </summary>
    internal void CloseConfigRowsForSelectionChange()
    {
        if (_isConfigTab) IsConfigTab = false;
        else _configTabState.Closed();
    }

    private ICommand? _showDetailsCommand;
    public ICommand ShowDetailsCommand => _showDetailsCommand ??= new RelayCommand(() => IsConfigTab = false);

    private ICommand? _showConfigCommand;
    public ICommand ShowConfigCommand => _showConfigCommand ??= new RelayCommand(() => IsConfigTab = true);

    // A plugin schema with 2+ top-level Group fields renders them as tabs (like the Hotkeys page)
    // instead of stacking every group's contents vertically down the page. A single group, or none,
    // isn't worth a tab bar, so those still render inline via ConfigFields as before.
    public bool HasMultipleConfigGroups => ConfigFields.Count(f => f.IsGroup) > 1;
    public List<PluginConfigFieldViewModel> ConfigGroups => ConfigFields.Where(f => f.IsGroup).ToList();
    public List<PluginConfigFieldViewModel> NonGroupConfigFields => ConfigFields.Where(f => !f.IsGroup).ToList();

    public ObservableCollection<PluginConfigFieldViewModel>? ActiveConfigGroupChildren
        => HasMultipleConfigGroups ? SelectedConfigGroup?.Children : null;

    public ObservableCollection<PluginConfigFieldViewModel>? FlatConfigFields
        => !HasMultipleConfigGroups ? ConfigFields : null;

    private PluginConfigFieldViewModel? _selectedConfigGroup;
    public PluginConfigFieldViewModel? SelectedConfigGroup
    {
        get
        {
            if (_selectedConfigGroup == null || !ConfigGroups.Contains(_selectedConfigGroup))
                _selectedConfigGroup = ConfigGroups.FirstOrDefault();
            return _selectedConfigGroup;
        }
        set
        {
            SetProperty(ref _selectedConfigGroup, value);
            OnPropertyChanged(nameof(ActiveConfigGroupChildren));
        }
    }

    private ICommand? _selectConfigGroupCommand;
    public ICommand SelectConfigGroupCommand => _selectConfigGroupCommand ??= new RelayCommand<PluginConfigFieldViewModel>(g => SelectedConfigGroup = g);
}

