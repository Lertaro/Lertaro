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
    public ObservableCollection<PluginConfigFieldViewModel> ConfigFields { get; }

    public bool HasConfigFields => ConfigFields.Count > 0;
    public bool HasNoComponents => RawComponents.Count == 0;

    // Plugin-wide select-all/deselect-all, toggling every component across every group at once --
    // separate from each PluginComponentGroupViewModel's own per-group toggle. Same single-item
    // exception as the per-group button.
    public bool HasToggleableComponents => RawComponents.Count(c => c.IsToggleable) > 1;
    public bool AreAllToggleableComponentsEnabled => RawComponents.Where(c => c.IsToggleable).All(c => c.IsEnabled);
    public string SelectAllToggleLabel => TranslationManager.Instance[AreAllToggleableComponentsEnabled ? "Common_DeselectAll" : "Common_SelectAll"];

    public ICommand ToggleAllComponentsCommand { get; }

    /// <summary>
    /// Raised when the plugin crosses the fully-disabled boundary (every toggleable component
    /// off, or back on again), so the owning list can move the card to its new sort position.
    /// </summary>
    public event Action<PluginInfoViewModel>? FullyDisabledChanged;

    private bool _isFullyDisabled;

    /// <summary>
    /// Whether every toggleable component of this plugin is currently disabled. A plugin with no
    /// toggleable components at all (translation/theme-only) can never be "fully disabled" --
    /// there is nothing the user turned off. Fully-disabled plugins sort after all others.
    /// </summary>
    public bool IsFullyDisabled
    {
        get => _isFullyDisabled;
        private set
        {
            if (SetProperty(ref _isFullyDisabled, value))
                FullyDisabledChanged?.Invoke(this);
        }
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

    /// <summary>
    /// Which of the pane's two tabs is showing: false for the plugin's details, true for its config.
    /// </summary>
    /// <remarks>
    /// Starts on details, so selecting a plugin shows what it is and what it provides rather than
    /// dropping straight into a form.
    ///
    /// Leaving the config tab rolls its fields back, which is what closing the old modal window did.
    /// Edits are only written by the tab's own OK button; anything abandoned by navigating away must not
    /// survive in the view models, or a later OK would write values the user thought they had discarded.
    /// </remarks>
    public bool IsConfigTab
    {
        get => _isConfigTab;
        set
        {
            if (_isConfigTab == value) return;
            if (!value) RollbackConfig();
            SetProperty(ref _isConfigTab, value);
        }
    }

    public Action? OnSave { get; }
    public Action? OnRollback { get; }

    public void RollbackConfig()
    {
        foreach (var field in ConfigFields)
            field.Reload();
        _selectedConfigGroup = ConfigGroups.FirstOrDefault();
        OnPropertyChanged(nameof(SelectedConfigGroup));
        OnPropertyChanged(nameof(ActiveConfigGroupChildren));
        OnPropertyChanged(nameof(FlatConfigFields));
        OnRollback?.Invoke();
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

