using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using Lertaro.App.Helpers;
using Lertaro.Core;
using Lertaro.Core.Indexer.NetworkDrive;
using Lertaro.App.Services;

using Lertaro.Core.Services.Search;

using Lertaro.App.ViewModels.Settings.General;
namespace Lertaro.App.ViewModels.Settings.NetworkDrive;

public class NetworkDriveSettingsViewModel : ViewModelBase
{
    private readonly PropertyChangedEventHandler _translationHandler;

    private readonly SearchService _searchService;
    private readonly Action _onTriggerFastRefresh;
    private readonly HashSet<string> _pendingRowRebuilds = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _observedRowRebuilds = new(StringComparer.OrdinalIgnoreCase);
    private string _networkIndexSummary = TranslationManager.Instance["Network_SummaryBusy"];
    private string _wslIndexSummary = TranslationManager.Instance["Network_SummaryBusy"];
    private string _folderIndexSummary = TranslationManager.Instance["Network_SummaryBusy"];
    private bool _canRebuildDrives;
    private bool _canRebuildWsl;
    private bool _canRebuildFolders;
    private bool _canAddFolder = true;
    private bool _isNetworkDrivesEmpty;
    private string _drivesPlaceholderText = string.Empty;
    private bool _hasPendingEdits;
    private ICommand? _addFolderCommand;
    private readonly LabeledOption[] _refreshModeOptions;

    public NetworkDriveSettingsViewModel(SearchService searchService, Action onTriggerFastRefresh)
    {
        _searchService = searchService;
        _onTriggerFastRefresh = onTriggerFastRefresh;
        RebuildDrivesCommand = new RelayCommand(
            () => NetworkDriveViewModelHelper.RebuildDrives(this, _searchService, _onTriggerFastRefresh),
            () => CanRebuildDrives);
        RebuildWslCommand = new RelayCommand(
            () => NetworkDriveViewModelHelper.RebuildWsl(this, _searchService, _onTriggerFastRefresh),
            () => CanRebuildWsl);
        RebuildFoldersCommand = new RelayCommand(
            () => NetworkDriveViewModelHelper.RebuildFolders(this, _searchService, _onTriggerFastRefresh),
            () => CanRebuildFolders);

        _refreshModeOptions =
        [
            new LabeledOption("Manual", TranslationManager.Instance["Network_ModeManual"]),
            new LabeledOption("15Minutes", TranslationManager.Instance["Network_Mode15M"]),
            new LabeledOption("Hourly", TranslationManager.Instance["Network_ModeHourly"]),
            new LabeledOption("Daily", TranslationManager.Instance["Network_ModeDaily"])
        ];

        _translationHandler = (s, e) =>
        {
            // Update labels in place -- the ComboBoxes' ItemsSource is bound to this same stable
            // array/reference and never gets reassigned, so their SelectedValue is never disturbed.
            _refreshModeOptions[0].Label = TranslationManager.Instance["Network_ModeManual"];
            _refreshModeOptions[1].Label = TranslationManager.Instance["Network_Mode15M"];
            _refreshModeOptions[2].Label = TranslationManager.Instance["Network_ModeHourly"];
            _refreshModeOptions[3].Label = TranslationManager.Instance["Network_ModeDaily"];

            foreach (var item in NetworkDrives)
            {
                item.NotifyLanguageChanged();
            }
            foreach (var item in WslDrives)
            {
                item.NotifyLanguageChanged();
            }
            foreach (var item in FolderIndexes)
            {
                item.NotifyLanguageChanged();
            }
        };
        TranslationManager.Instance.PropertyChanged += _translationHandler;

    }

    public ObservableCollection<NetworkDriveSettingsItem> NetworkDrives { get; } = new();
    public ObservableCollection<WslSettingsItem> WslDrives { get; } = new();
    public ObservableCollection<FolderIndexSettingsItem> FolderIndexes { get; } = new();
    public bool IsFolderIndexesEmpty => FolderIndexes.Count == 0;
    // Companion bool for XAML Visibility bindings that need the opposite of IsFolderIndexesEmpty --
    // there's no inverting BoolToVisibilityConverter registered in IndexSettingsPage.xaml.
    public bool HasFolderIndexes => !IsFolderIndexesEmpty;

    public ICommand AddFolderCommand => _addFolderCommand ??= new RelayCommand(
        () => NetworkDriveFolderHelper.AddFolder(this, _searchService, _onTriggerFastRefresh, _pendingRowRebuilds, _observedRowRebuilds));

    // Called from NetworkDriveViewModelHelper.RunFolderIndexAction's Delete branch -- removes the row
    // from view entirely (not just resetting its RowAction, since there's nothing left to show for it).
    internal void RemoveFolderIndex(FolderIndexSettingsItem item)
    {
        item.PropertyChanged -= OnFolderItemChanged;
        FolderIndexes.Remove(item);
        OnPropertyChanged(nameof(IsFolderIndexesEmpty));
    }

    internal void NotifyFolderIndexesEmptyChanged() => OnPropertyChanged(nameof(IsFolderIndexesEmpty));

    public bool HasPendingEdits { get => _hasPendingEdits; internal set => SetProperty(ref _hasPendingEdits, value); }
    public bool IsWslPanelVisible => WslDrives.Count > 0;

    public IReadOnlyList<LabeledOption> RefreshModeOptions => _refreshModeOptions;

    // Each of NetworkDrives/WslDrives/FolderIndexes gets its own Rebuild command, summary text, and
    // enablement -- these three categories share this one ViewModel/page but their scan state, item
    // counts, and busy-ness must never bleed into each other's display or actions.
    public ICommand RebuildDrivesCommand { get; }
    public ICommand RebuildWslCommand { get; }
    public ICommand RebuildFoldersCommand { get; }

    public string NetworkIndexSummary { get => _networkIndexSummary; set => SetProperty(ref _networkIndexSummary, value); }
    public string WslIndexSummary { get => _wslIndexSummary; set => SetProperty(ref _wslIndexSummary, value); }
    public string FolderIndexSummary { get => _folderIndexSummary; set => SetProperty(ref _folderIndexSummary, value); }

    public bool CanRebuildDrives
    {
        get => _canRebuildDrives;
        set { if (SetProperty(ref _canRebuildDrives, value)) CommandManager.InvalidateRequerySuggested(); }
    }

    public bool CanRebuildWsl
    {
        get => _canRebuildWsl;
        set { if (SetProperty(ref _canRebuildWsl, value)) CommandManager.InvalidateRequerySuggested(); }
    }

    public bool CanRebuildFolders
    {
        get => _canRebuildFolders;
        set { if (SetProperty(ref _canRebuildFolders, value)) CommandManager.InvalidateRequerySuggested(); }
    }

    // Deliberately just !folderBusy, not CanRebuildFolders itself -- CanRebuildFolders is also false
    // whenever nothing is AppliedEnabled yet (e.g. a folder just added and not applied), which would
    // disable Add right after adding your first folder, before you'd ever get a chance to add a second one.
    public bool CanAddFolder { get => _canAddFolder; internal set => SetProperty(ref _canAddFolder, value); }

    public bool IsNetworkDrivesEmpty { get => _isNetworkDrivesEmpty; set => SetProperty(ref _isNetworkDrivesEmpty, value); }
    public string DrivesPlaceholderText { get => _drivesPlaceholderText; set => SetProperty(ref _drivesPlaceholderText, value); }

    private (UserSettings settings, IReadOnlyList<NetworkIndexStatus>? statuses, bool isGlobalBusy)? _pendingRefresh;
    private bool _refreshInFlight;

    // Entry point, called from SettingsViewModel.ApplyUiState on every indexer status push (up to ~10/sec
    // while a drive is actively indexing). The data gathering below (NetworkDriveResolver's
    // WNetGetConnection/DriveInfo.IsReady, and especially the WSL \\wsl$\<distro> probe) does blocking
    // syscalls that can each take a noticeable amount of time, so it now runs on a background thread --
    // only the final ObservableCollection update happens back on the UI thread. Only ever called from the
    // UI thread (ApplyUiState always runs inside a Dispatcher.BeginInvoke), so _pendingRefresh/
    // _refreshInFlight need no locking: every read/write of them happens on that same thread, just
    // interleaved across await continuations.
    public void RefreshNetworkDrives(UserSettings userSettings, IReadOnlyList<NetworkIndexStatus>? indexStatuses = null, bool isGlobalBusy = false)
    {
        _pendingRefresh = (userSettings, indexStatuses, isGlobalBusy);
        if (_refreshInFlight) return; // a gather is already running; it'll pick up this latest request when it loops
        _refreshInFlight = true;
        _ = RunRefreshLoopAsync();
    }

    private async Task RunRefreshLoopAsync()
    {
        try
        {
            while (_pendingRefresh is { } request)
            {
                _pendingRefresh = null;
                try
                {
                    var data = await Task.Run(() => NetworkDriveRefreshCoordinator.GatherData(request.settings, request.statuses, _searchService));
                    NetworkDriveRefreshCoordinator.ApplyGatheredData(this, _searchService, _onTriggerFastRefresh, _pendingRowRebuilds, _observedRowRebuilds, request.settings, request.statuses, request.isGlobalBusy, data);
                }
                catch (Exception ex)
                {
                    // Never let one bad refresh (e.g. a transient network-resolution failure) permanently
                    // wedge _refreshInFlight -- that would silently stop this Settings window from ever
                    // refreshing network drive state again for the rest of its lifetime.
                    Logger.Log($"[NetworkDriveSettingsViewModel] Network drive refresh failed: {ex.Message}", LogLevel.Error);
                }
            }
        }
        finally
        {
            _refreshInFlight = false;
        }
    }

    public void ResetPendingEdits() => HasPendingEdits = false;

    // Called from NetworkDriveRefreshCoordinator.ApplyGatheredData once it's done updating the row
    // collections -- OnPropertyChanged is protected, so this small wrapper is what lets that (external,
    // same-assembly) helper class raise these three notifications.
    internal void NotifyRefreshResultChanged()
    {
        OnPropertyChanged(nameof(IsWslPanelVisible));
        OnPropertyChanged(nameof(IsFolderIndexesEmpty));
        OnPropertyChanged(nameof(HasFolderIndexes));
    }

    internal void OnNetworkDriveItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(NetworkDriveSettingsItem.IsEnabled) or nameof(NetworkDriveSettingsItem.RefreshMode)) HasPendingEdits = true;
    }

    internal void OnWslDriveItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(WslSettingsItem.IsEnabled) or nameof(WslSettingsItem.RefreshMode)) HasPendingEdits = true;
    }

    internal void OnFolderItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(FolderIndexSettingsItem.IsEnabled) or nameof(FolderIndexSettingsItem.RefreshMode)) HasPendingEdits = true;
    }

    // appliedEnabled is always recomputed by the caller from the current UserSettings.NetworkDrives/
    // WslSettings (the same way LocalDriveSettingsViewModel.UpdateStatus derives its own local appliedEnabled
    // every call), never read back off a previously-stored field -- so RowAction can't go stale just because
    // some save path forgot to sync a cached "applied" flag afterwards.
    // Shared by all three row categories -- only what counts as "eligible for Delete once un-applied"
    // differs: a drive/WSL row only if the scan cache still remembers its key, a folder row always
    // (see NetworkDriveFolderHelper's caller for why).
    internal void UpdateRowAction<TItem>(TItem item, bool appliedEnabled, string? state, Func<TItem, bool> canDeleteWhenUnapplied) where TItem : INetworkRowItem
    {
        item.AppliedEnabled = appliedEnabled;
        item.RowAction = appliedEnabled
            ? (state == "indexing" ? NetworkDriveRowAction.Stop : NetworkDriveRowAction.Rebuild)
            : canDeleteWhenUnapplied(item) ? NetworkDriveRowAction.Delete : NetworkDriveRowAction.None;
    }

    internal void TrackPendingRebuild(string drive, string? state)
    {
        if (!_pendingRowRebuilds.Contains(drive)) return;
        if (state == "indexing") _observedRowRebuilds.Add(drive);
        else if (_observedRowRebuilds.Contains(drive))
        {
            _pendingRowRebuilds.Remove(drive);
            _observedRowRebuilds.Remove(drive);
        }
    }

    public void Cleanup() => TranslationManager.Instance.PropertyChanged -= _translationHandler;
}
