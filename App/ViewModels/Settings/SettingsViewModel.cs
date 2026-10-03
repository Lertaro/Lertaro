using System.Windows.Input;
using Lertaro.App.Helpers;
using Lertaro.App.Services;
using Lertaro.Core;
using System.ComponentModel;
using Lertaro.App.ViewModels.Settings.Plugins;
using Lertaro.Core.Services.Search;

using Lertaro.App.Services.Plugin;
using Lertaro.Core.Wire;
using Lertaro.App.ViewModels.Settings.LocalDrive;
using Lertaro.App.ViewModels.Settings.NetworkDrive;
using Lertaro.App.ViewModels.Settings.General;
namespace Lertaro.App.ViewModels.Settings;

public class SettingsViewModel : ViewModelBase
{
    private readonly SearchService _searchService = new();
    private readonly UserSettings _userSettings = UserSettings.Load();
    private readonly SettingsStatusMonitor _statusMonitor;
    private bool _canApply = true;
    private bool _isBusy;
    private bool _isServiceReady = true;
    private int _bindingErrorCount;

    public SettingsViewModel()
    {
        // The rest stay EAGER: their constructors are just command/field wiring and in-memory copies, and
        // ApplyUiState (every 5s, plus up to ~10 status pushes a second while a drive indexes) reads
        // several of them -- deferring those would trade one open cost for a recurring one.
        Service = new ServiceSettingsViewModel(_searchService, RefreshLists);
        LocalDrive = new LocalDriveSettingsViewModel(_searchService, RefreshLists);
        NetworkDrive = new NetworkDriveSettingsViewModel(_searchService, RefreshLists);
        General = new GeneralSettingsViewModel(_userSettings);
        Validation = new SettingsValidationGate(General, () => _plugins);
        Exclusions = new ExclusionSettingsViewModel(_userSettings);
        Blacklist = new BlacklistSettingsViewModel(_userSettings);
        Hotkeys = new HotkeySettingsViewModel(_userSettings, Blacklist);
        Favorites = new FavoritesSettingsViewModel(_userSettings);
        QuickLaunch = new QuickLaunchSettingsViewModel(_userSettings);
        QuickPanel = new QuickPanel.QuickPanelSettingsViewModel(_userSettings);
        LocalSend = new LocalSend.LocalSendSettingsViewModel(_userSettings);
        RefreshCommand = new RelayCommand(Refresh);
        ApplyCommand = new RelayCommand(() => Apply(), () => CanApply);
        _deferred = new DeferredSettingsViewModels(_userSettings, _searchService);
        _statusMonitor = new SettingsStatusMonitor(_searchService, ApplyUiState);
        TranslationManager.Instance.PropertyChanged += OnLanguageChanged;
        RefreshLists();
    }

    // The three DEFERRED sub-VMs (log reader, themes, history lists) live in their own holder.
    private readonly DeferredSettingsViewModels _deferred;
    public ServiceLogViewModel Log => _deferred.Log;
    public ThemeSettingsViewModel Appearance => _deferred.Appearance;
    public HistorySettingsViewModel History => _deferred.History;

    // The Quick Panel page is nudged from here rather than subscribing itself: its labels are built in
    // code (the kind dropdown's options, a plugin tab's name) instead of bound through the XAML
    // translation markup that repaints itself, so nothing else would tell them the language moved.
    private void OnLanguageChanged(object? sender, PropertyChangedEventArgs e)
    {
        ApplyUiState();
        QuickPanel.NotifyLanguageChanged();
        QuickLaunch.NotifyLanguageChanged();
        _deferred.ExistingHistory?.NotifyLanguageChanged();
    }

    public ServiceSettingsViewModel Service { get; }
    public LocalDriveSettingsViewModel LocalDrive { get; }
    public NetworkDriveSettingsViewModel NetworkDrive { get; }
    public GeneralSettingsViewModel General { get; }
    public ExclusionSettingsViewModel Exclusions { get; }

    // Lazy, not built alongside the other sub-VMs above -- issue #186: PluginManagementViewModel's ctor
    // runs PluginLoaderHelper.BuildPluginList, which does genuine reflection (AppDomain.GetAssemblies,
    // GetReferencedAssemblies, and two GetTypes() scans per plugin DLL via GetPluginDisplayName/
    // ResolveConfigurable) across every loaded plugin -- unlike every other sub-VM here, which is cheap
    // field/command wiring or LINQ over PluginManager's already-cached collections. Deferring it means a
    // Settings-window open that never visits the Plugins tab (or types a plugin name into the search box,
    // which forces it via the property access in SettingsWindowSearchExtensions.BuildAllEntries) never
    // pays that scan at all.
    private PluginManagementViewModel? _plugins;
    public PluginManagementViewModel Plugins => _plugins ??= new PluginManagementViewModel(_userSettings);

    public HotkeySettingsViewModel Hotkeys { get; }
    public BlacklistSettingsViewModel Blacklist { get; }
    public FavoritesSettingsViewModel Favorites { get; }
    public QuickLaunchSettingsViewModel QuickLaunch { get; }
    public LocalSend.LocalSendSettingsViewModel LocalSend { get; }

    /// <summary>
    /// The floating panel's own page. Its "tabs" are workspaces, which is not what a tab means in the
    /// panel's own strip -- see QuickPanelSettingsViewModel.
    /// </summary>
    public QuickPanel.QuickPanelSettingsViewModel QuickPanel { get; }
    public ICommand RefreshCommand { get; }
    public ICommand ApplyCommand { get; }

    public bool CanApply
    {
        get => _canApply;
        private set { if (SetProperty(ref _canApply, value)) CommandManager.InvalidateRequerySuggested(); }
    }

    /// <summary>
    /// Reports the window's binding-level error count, from WPF's own Validation.Error. Routed through the
    /// view model rather than assigned onto CanApply directly, because the gate has more than one input --
    /// writing the flag from the window used to erase whatever service readiness had set it to, and be
    /// erased by it in turn, so whichever ran last won.
    /// </summary>
    public void SetBindingErrorCount(int count)
    {
        if (_bindingErrorCount == count)
            return;
        _bindingErrorCount = count;
        RefreshCanApply();
    }

    // Deliberately not including ValidationErrors: this is the BUTTON's state, and re-reading the pages'
    // errors here would put that walk on the status-push path (ApplyUiState runs up to ~10x/s while a
    // drive indexes) to keep a cosmetic flag fresh. Apply() refuses on those errors itself, and the page
    // that raised one is already showing it next to the field.
    private void RefreshCanApply() => CanApply = _bindingErrorCount == 0 && _isServiceReady;

    /// <summary>
    /// Every validation error the settings pages are currently showing.
    ///
    /// WPF's Validation.Error only fires for rules expressed in a binding -- IDataErrorInfo, exception
    /// validation, converters -- so a rule a page works out for itself (a trigger character the search
    /// syntax would consume, two plugins claiming the same prefix) never reached Apply, which would then
    /// save a value the page was visibly reporting as broken.
    ///
    /// Only pages already constructed are asked. An unvisited page holds no staged edit and so can report
    /// no error, and going through a lazy property to ask would construct it (see Plugins) purely to be
    /// told so.
    /// </summary>
    /// <summary>
    /// Every blocking error the settings pages are currently showing, and the status-bar reason shown when
    /// Apply refused because of them. Public because SettingsWindow.xaml binds it -- see the type's own
    /// comment on why a binding path needs public members.
    /// </summary>
    public SettingsValidationGate Validation { get; }

    /// <summary>The gate's own list, kept as the name callers and tests already read.</summary>
    public IReadOnlyList<string> ValidationErrors => Validation.Errors;

    public bool IsBusy { get => _isBusy; set => SetProperty(ref _isBusy, value); }

    public bool IsServiceReady
    {
        get => _isServiceReady;
        set { if (SetProperty(ref _isServiceReady, value)) RefreshCanApply(); }
    }

    private bool _isSaved;

    public void Cleanup()
    {
        _statusMonitor.Dispose();
        TranslationManager.Instance.PropertyChanged -= OnLanguageChanged;
        // Null-conditional: a window closed without visiting these tabs must not construct them to dispose.
        _deferred.ExistingLog?.Dispose();
        _searchService.Dispose();
        General.Cleanup();
        _deferred.ExistingAppearance?.Cleanup();
        _deferred.ExistingHistory?.Cleanup();
        LocalDrive.Cleanup();
        NetworkDrive.Cleanup();
        Hotkeys.Cleanup();
        _plugins?.Cleanup();

        if (!_isSaved)
        {
            UserSettings.ForceReload();
            if (_plugins != null)
            {
                foreach (var plugin in _plugins.Plugins)
                {
                    plugin.RollbackConfig();
                }
            }
        }
    }

    public void Refresh() => RefreshLists();

    public void RefreshLists() => _statusMonitor.RefreshLists();

    /// <summary>
    /// Commits every staged edit, and reports whether it did. The Settings window's OK uses the answer to
    /// decide whether it may close -- see BtnOk_Click.
    /// </summary>
    public bool Apply()
    {
        if (!CanApply)
            return false;

        // Second gate, and deliberately here rather than only on the button: CanApply is bound to the
        // button's IsEnabled, which several unrelated things drive, while this method is also reached
        // directly from OK. Saving a value the page is reporting as broken is worse than not saving -- the
        // user has already been told what is wrong, and the previous, working value would be silently
        // replaced by one that never fires.
        var errors = ValidationErrors;
        if (errors.Count > 0)
        {
            Logger.Log($"[SettingsViewModel] Apply refused: {errors.Count} setting error(s): {string.Join(" | ", errors)}", LogLevel.Warn);
            Validation.Refuse(errors.Count);
            return false;
        }

        Validation.Clear();

        _isSaved = true;

        var snapshot = SettingsApplySnapshot.Capture(_userSettings, LocalDrive, NetworkDrive);

        _userSettings.NetworkDrives = snapshot.NewNetworkDrives;
        _userSettings.WslSettings = snapshot.NewWslDrives;
        _userSettings.FolderIndexes = snapshot.NewFolderIndexes;
        Exclusions.Save();
        General.Apply();
        // _plugins, not the Plugins property: an untouched Plugins tab was never constructed, so it has
        // nothing dirty to save -- going through the property here would force that reflection scan
        // (see the Plugins property's own comment) just to immediately no-op.
        _plugins?.Save();
        Hotkeys.Apply();
        Blacklist.Save();
        // _history, not History: an untouched History tab was never constructed, so there is nothing
        // staged to save -- going through the property would construct it (loading both history files)
        // purely to write back what it already read.
        _deferred.ExistingHistory?.Save();
        Favorites.Save(); SettingsApplyHelpers.RebindFavoriteHotkeys(Favorites);
        QuickLaunch.Save();
        QuickPanel.Save();
        LocalSend.Apply();
        _userSettings.Save();
        Core.Services.LocalSend.LocalSendServiceManager.Instance.ApplySettings(_userSettings);
        App.HookClient?.SendMessage(new IpcMessage { Id = IpcMessageId.ReloadSettings });
        PluginManager.Instance.RefreshDisabledComponents();
        InlineSearchManager.Instance.ExplorerTracker.RefreshActiveWindowAdapters();
        NetworkDrive.ResetPendingEdits();
        // Favorites/quick-launch edits must reach search windows that are already open: the quick
        // window's launch panel otherwise only rebuilds on its next show, and its live result list
        // keeps the rows the previous query read -- see OpenSearchWindowRefresher.
        OpenSearchWindowRefresher.AfterSettingsSaved();
        var exclusionsChanged = SettingsChangeSnapshot.ExclusionsChanged(snapshot.PreviousExclusions, SettingsChangeSnapshot.CaptureExclusions(_userSettings));
        var newDisabledAliases = _userSettings.DisabledPluginComponents
            .Where(c => c.Contains("::AliasProvider::", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var aliasProviderEnabled = snapshot.PreviousDisabledAliases.Any(c => !newDisabledAliases.Contains(c, StringComparer.OrdinalIgnoreCase));

        // Everything above is the UI-side commit, which has already happened; this is the service side,
        // which does not block the window -- see SettingsApplyBackground.
        new SettingsApplyBackground(_searchService, snapshot, exclusionsChanged, aliasProviderEnabled, RefreshLists).Run();

        return true;
    }

    private void ApplyUiState()
    {
        var status = _statusMonitor.LatestStatus;
        var settings = _statusMonitor.LatestMachineSettings;
        var networkStatuses = _statusMonitor.LatestNetworkStatuses;
        var isServiceReady = status.State != "error";
        Service.UpdateStatus(status);
        LocalDrive.UpdateStatus(status, settings);
        // Network settings come from UserSettings.Load() (a separate local file, read once at startup)
        // and network indexing is its own subsystem -- neither depends on the local USN indexer's own
        // lifecycle. The only thing that legitimately blocks network settings from a "service"
        // perspective is not being able to reach the service at all.
        NetworkDrive.RefreshNetworkDrives(_userSettings, networkStatuses, !isServiceReady);
        // The WSL tab hides itself once its drive list empties out (e.g. the last distro was removed).
        // If it was the active tab, fall back to Network so the page never lands on a hidden tab.
        if (LocalDrive.SelectedTab == "Wsl" && !NetworkDrive.IsWslPanelVisible)
            LocalDrive.SelectedTab = "Network";
        // The shared Apply/OK button only needs the service to be reachable: MachineSettings is loaded
        // synchronously at SearchEngine construction, before the indexer's own loading-cache/indexing/
        // pending lifecycle even starts, so an active scan or cache load never means the data Apply()
        // would read and save is stale or empty -- only an unreachable service does (RefreshLists()
        // falls back to an empty MachineSettings() in that case).
        IsServiceReady = isServiceReady;
        _deferred.ExistingLog?.IsServiceReady = isServiceReady;
        IsBusy = !isServiceReady;
        // CanApply follows from IsServiceReady (see its own comment); nothing to assign here.
    }
}
