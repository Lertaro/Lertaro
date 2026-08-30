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

    public SettingsViewModel()
    {
        Service = new ServiceSettingsViewModel(_searchService, RefreshLists);
        Log = new ServiceLogViewModel(_searchService);

        LocalDrive = new LocalDriveSettingsViewModel(_searchService, RefreshLists);

        NetworkDrive = new NetworkDriveSettingsViewModel(_searchService, RefreshLists);
        General = new GeneralSettingsViewModel(_userSettings);
        Appearance = new ThemeSettingsViewModel(_userSettings);
        Exclusions = new ExclusionSettingsViewModel(_userSettings);
        Blacklist = new BlacklistSettingsViewModel(_userSettings);
        Hotkeys = new HotkeySettingsViewModel(_userSettings, Blacklist);
        History = new HistorySettingsViewModel(_userSettings);
        Favorites = new FavoritesSettingsViewModel(_userSettings);
        QuickLaunch = new QuickLaunchSettingsViewModel(_userSettings);
        QuickPanel = new QuickPanel.QuickPanelSettingsViewModel(_userSettings);
        LocalSend = new LocalSend.LocalSendSettingsViewModel(_userSettings);
        RefreshCommand = new RelayCommand(Refresh);
        ApplyCommand = new RelayCommand(Apply, () => CanApply);

        _statusMonitor = new SettingsStatusMonitor(_searchService, ApplyUiState);
        TranslationManager.Instance.PropertyChanged += OnLanguageChanged;
        RefreshLists();
    }

    // The Quick Panel page is nudged from here rather than subscribing itself: its labels are built in
    // code (the kind dropdown's options, a plugin tab's name) instead of bound through the XAML
    // translation markup that repaints itself, so nothing else would tell them the language moved.
    private void OnLanguageChanged(object? sender, PropertyChangedEventArgs e)
    {
        ApplyUiState();
        QuickPanel.NotifyLanguageChanged();
        QuickLaunch.NotifyLanguageChanged();
    }

    public ServiceSettingsViewModel Service { get; }
    public ServiceLogViewModel Log { get; }
    public LocalDriveSettingsViewModel LocalDrive { get; }
    public NetworkDriveSettingsViewModel NetworkDrive { get; }
    public GeneralSettingsViewModel General { get; }
    public ThemeSettingsViewModel Appearance { get; }
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
    public HistorySettingsViewModel History { get; }
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
        set { if (SetProperty(ref _canApply, value)) CommandManager.InvalidateRequerySuggested(); }
    }

    public bool IsBusy { get => _isBusy; set => SetProperty(ref _isBusy, value); }
    public bool IsServiceReady { get => _isServiceReady; set => SetProperty(ref _isServiceReady, value); }

    private bool _isSaved;

    public void Cleanup()
    {
        _statusMonitor.Dispose();
        TranslationManager.Instance.PropertyChanged -= OnLanguageChanged;
        Log.Dispose();
        _searchService.Dispose();
        General.Cleanup();
        Appearance.Cleanup();
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

    public void Apply()
    {
        if (!CanApply)
            return;

        _isSaved = true;

        var previousNetworkDrives = _userSettings.NetworkDrives
            .Select(d => new NetworkDriveSetting { Id = d.Id, RefreshMode = d.RefreshMode })
            .ToList();
        var previousWslDrives = _userSettings.WslSettings
            .Select(w => new WslSetting { Id = w.Id, RefreshMode = w.RefreshMode })
            .ToList();
        var previousFolderIndexes = _userSettings.FolderIndexes
            .Select(f => new FolderIndexSetting { Path = f.Path, RefreshMode = f.RefreshMode })
            .ToList();
        var previousExclusions = SettingsChangeSnapshot.CaptureExclusions(_userSettings);
        var previousDisabledAliases = _userSettings.DisabledPluginComponents
            .Where(c => c.Contains("::AliasProvider::", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var machineSettings = new MachineSettings
        {
            LocalDrives = LocalDrive.LocalDrives.Where(d => d.IsEnabled && !string.IsNullOrWhiteSpace(d.Id)).Select(d => d.Id).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
        };

        var newNetworkDrives = NetworkDrive.NetworkDrives.Where(d => d.IsEnabled && !string.IsNullOrWhiteSpace(d.Id)).Select(d => new NetworkDriveSetting
        {
            Id = d.Id,
            RefreshMode = d.RefreshMode
        }).ToList();
        var newWslDrives = NetworkDrive.WslDrives.Where(w => w.IsEnabled && !string.IsNullOrWhiteSpace(w.Id)).Select(w => new WslSetting
        {
            Id = w.Id,
            RefreshMode = w.RefreshMode
        }).ToList();
        var newFolderIndexes = NetworkDrive.FolderIndexes.Where(f => f.IsEnabled && !string.IsNullOrWhiteSpace(f.Path)).Select(f => new FolderIndexSetting
        {
            Path = f.Path,
            RefreshMode = f.RefreshMode
        }).ToList();
        var localDriveSnapshots = LocalDrive.LocalDrives
            .Select(d => new LocalDriveSnapshot(d.Drive, d.Id, d.IsEnabled))
            .ToList();
        _userSettings.NetworkDrives = newNetworkDrives;
        _userSettings.WslSettings = newWslDrives;
        _userSettings.FolderIndexes = newFolderIndexes;
        Exclusions.Save();
        General.Apply();
        // _plugins, not the Plugins property: an untouched Plugins tab was never constructed, so it has
        // nothing dirty to save -- going through the property here would force that reflection scan
        // (see the Plugins property's own comment) just to immediately no-op.
        _plugins?.Save();
        Hotkeys.Apply();
        Blacklist.Save();
        History.Save();
        Favorites.Save();
        QuickLaunch.Save();
        QuickPanel.Save();
        LocalSend.Apply();
        _userSettings.Save();
        Core.Services.LocalSend.LocalSendServiceManager.Instance.ApplySettings(_userSettings);
        App.HookClient?.SendMessage(new IpcMessage { Id = IpcMessageId.ReloadSettings });
        PluginManager.Instance.RefreshDisabledComponents();
        InlineSearchManager.Instance.ExplorerTracker.RefreshActiveWindowAdapters();
        NetworkDrive.ResetPendingEdits();
        var exclusionsChanged = SettingsChangeSnapshot.ExclusionsChanged(previousExclusions, SettingsChangeSnapshot.CaptureExclusions(_userSettings));
        var newDisabledAliases = _userSettings.DisabledPluginComponents
            .Where(c => c.Contains("::AliasProvider::", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var aliasProviderEnabled = previousDisabledAliases.Any(c => !newDisabledAliases.Contains(c, StringComparer.OrdinalIgnoreCase));

        _ = Task.Run(async () =>
        {
            try
            {
            var previousLocalDrives = (await _searchService.GetMachineSettingsAsync()).LocalDrives.ToList();
            if (SettingsChangeSnapshot.StringListChanged(previousLocalDrives, machineSettings.LocalDrives))
                await _searchService.SaveMachineSettingsAsync(machineSettings);

            if (exclusionsChanged)
            {
                _searchService.RefreshNetworkIndexes();
            }
            else if (SettingsApplyHelpers.NetworkSettingsChanged(previousNetworkDrives, newNetworkDrives)
                || SettingsApplyHelpers.WslSettingsChanged(previousWslDrives, newWslDrives)
                || SettingsApplyHelpers.FolderIndexesChanged(previousFolderIndexes, newFolderIndexes))
            {
                await NetworkDriveApplyHelper.ApplyChangesAsync(_searchService, previousNetworkDrives, newNetworkDrives);
                foreach (var wsl in newWslDrives)
                {
                    if (!previousWslDrives.Any(w => w.Id.Equals(wsl.Id, StringComparison.OrdinalIgnoreCase)))
                    {
                        var unc = $@"\\wsl$\{wsl.Id}";
                        _searchService.RefreshNetworkDriveIndex(unc);
                    }
                }
                // Unlike a network drive, a folder path never needs resolving from the OS, so there's
                // nothing to wait for -- ConfigureNetworkIndexes() (already called above via
                // ApplyChangesAsync) already auto-queues an initial refresh for it; this just requests it
                // directly, same as a newly-added WSL distro above.
                foreach (var folder in newFolderIndexes)
                {
                    if (!previousFolderIndexes.Any(f => f.Path.Equals(folder.Path, StringComparison.OrdinalIgnoreCase)))
                        _searchService.RefreshNetworkDriveIndex(folder.Path);
                }
            }

            if (exclusionsChanged)
                await SettingsApplyHelpers.RebuildScanBasedLocalDrivesAsync(_searchService, localDriveSnapshots, machineSettings.LocalDrives);

            if (aliasProviderEnabled)
                await _searchService.InitializeOrLoadIndexAsync(false);
            }
            catch (Exception ex)
            {
                Logger.Log($"[Settings] Apply pipeline failed: {ex}", LogLevel.Error);
            }
            finally
            {
                RefreshLists();
            }
        });
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
        Log.IsServiceReady = isServiceReady;
        IsBusy = !isServiceReady;
        CanApply = isServiceReady;
    }

}
