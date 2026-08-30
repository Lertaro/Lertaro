using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Input;
using Lertaro.App.Helpers;
using Lertaro.App.Services;
using Lertaro.Core;
using Lertaro.Core.Indexer.Usn;

using Lertaro.Core.Services.Search;

namespace Lertaro.App.ViewModels.Settings.LocalDrive;

public class LocalDriveSettingsViewModel : ViewModelBase
{
    private readonly PropertyChangedEventHandler _translationHandler;

    private readonly SearchService _searchService;
    private readonly Action _onTriggerFastRefresh;
    private readonly HashSet<string> _pendingRowRebuilds = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _observedRowRebuilds = new(StringComparer.OrdinalIgnoreCase);
    private string _indexSummary = TranslationManager.Instance["Local_LoadingInfo"];
    private bool _canRebuild;
    private bool _isLocalDrivesEmpty = false;
    private string _drivesPlaceholderText = "";
    private bool _isBusy;
    private bool _isDriveCheckboxEnabled;

    public LocalDriveSettingsViewModel(SearchService searchService, Action onTriggerFastRefresh)
    {
        _searchService = searchService;
        _onTriggerFastRefresh = onTriggerFastRefresh;
        RebuildCommand = new RelayCommand(Rebuild, () => CanRebuild);

        // RowActionText only re-evaluates when RowAction itself changes value, so a language switch
        // otherwise leaves the per-row Rebuild/Delete button text stuck in the old language until the
        // action type actually changes (which may never happen while the page is open).
        _translationHandler = (s, e) =>
        {
            foreach (var item in LocalDrives)
                item.NotifyLanguageChanged();
        };
        TranslationManager.Instance.PropertyChanged += _translationHandler;

    }

    public ObservableCollection<LocalDriveSettingsItem> LocalDrives { get; } = new();
    public ICommand RebuildCommand { get; }

    // Tab navigation for the merged "Index" settings page (Local/Network share one page now).
    private string _selectedTab = "Local";
    public string SelectedTab
    {
        get => _selectedTab;
        set => SetProperty(ref _selectedTab, value);
    }

    private ICommand? _selectTabCommand;
    public ICommand SelectTabCommand => _selectTabCommand ??= new RelayCommand<string>(tab => SelectedTab = tab);

    public string IndexSummary
    {
        get => _indexSummary;
        set => SetProperty(ref _indexSummary, value);
    }

    public bool CanRebuild
    {
        get => _canRebuild;
        set
        {
            if (SetProperty(ref _canRebuild, value))
                CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool IsLocalDrivesEmpty
    {
        get => _isLocalDrivesEmpty;
        set => SetProperty(ref _isLocalDrivesEmpty, value);
    }

    public string DrivesPlaceholderText
    {
        get => _drivesPlaceholderText;
        set => SetProperty(ref _drivesPlaceholderText, value);
    }

    public bool IsUserAdmin => ElevationHelper.IsUserAdmin();

    public bool IsDriveCheckboxEnabled
    {
        get => _isDriveCheckboxEnabled;
        set => SetProperty(ref _isDriveCheckboxEnabled, value);
    }

    public void UpdateStatus(UsnIndexer.IndexerStatus status, MachineSettings settings)
    {
        foreach (var drive in status.Drives.OrderBy(d => d.Drive))
        {
            var item = LocalDrives.FirstOrDefault(d => d.Drive.Equals(drive.Drive, StringComparison.OrdinalIgnoreCase));
            var isPresent = drive.State != "unavailable";
            var driveId = VolumeHelper.GetVolumeId(drive.Drive) ?? string.Empty;
            var appliedEnabled = isPresent && settings.IsLocalDriveEnabled(driveId);
            var isEnabled = item?.IsEnabled ?? appliedEnabled;
            if (item == null)
            {
                item = new LocalDriveSettingsItem
                {
                    Drive = drive.Drive,
                    Id = driveId,
                    Name = $"{drive.Drive}:",
                    IsEnabled = isEnabled
                };
                item.RowActionCommand = new RelayCommand(() => RunDriveAction(item), () => item.CanRunRowAction);
                item.PropertyChanged += OnLocalDriveItemChanged;
                LocalDrives.Add(item);
            }
            item.Id = driveId;

            var hasCache = !string.IsNullOrWhiteSpace(drive.CachePath) && File.Exists(drive.CachePath);
            TrackPendingRebuild(drive);

            // A journal-backed (true NTFS/$MFT) rebuild has no cancellation support -- MftIndexScanner is a
            // raw $MFT parse, not a walk, with no natural interruption point. Every other kind (ReFS, the
            // non-journal LocalDriveWalkBuilder fallback) does, so Stop only shows for those. drive.Kind is
            // read here BEFORE it gets overwritten with its translated display form below.
            var canStop = drive.State == "indexing" && !string.Equals(drive.Kind, "NTFS", StringComparison.OrdinalIgnoreCase);
            item.RowAction = canStop ? LocalDriveRowAction.Stop
                : appliedEnabled ? LocalDriveRowAction.Rebuild : hasCache ? LocalDriveRowAction.Delete : LocalDriveRowAction.None;
            item.CanRunRowAction = item.RowAction == LocalDriveRowAction.Stop
                || (_pendingRowRebuilds.Count == 0 && (item.RowAction == LocalDriveRowAction.Delete || CanRebuild && item.RowAction == LocalDriveRowAction.Rebuild));
            item.CanEditEnabled = isPresent && IsDriveCheckboxEnabled;
            item.CachePath = drive.CachePath;
            item.Kind = drive.Kind == "LocalNtfs" ? TranslationManager.Instance["Local_KindLocalNtfs"] : drive.Kind;
            item.Strategy = appliedEnabled ? TranslationManager.Instance["Local_StrategyMftUsn"] : TranslationManager.Instance["Local_StrategyDisabled"];
            item.State = LocalDriveSettingsHelper.TranslateState(drive.State);
            item.ItemCount = appliedEnabled && drive.Files + drive.Dirs > 0 ? $"{drive.Files + drive.Dirs:N0}" : "-";
        }

        for (var i = LocalDrives.Count - 1; i >= 0; i--)
        {
            if (!status.Drives.Any(d => d.Drive.Equals(LocalDrives[i].Drive, StringComparison.OrdinalIgnoreCase)))
            {
                LocalDrives[i].PropertyChanged -= OnLocalDriveItemChanged;
                LocalDrives.RemoveAt(i);
            }
        }

        IsLocalDrivesEmpty = LocalDrives.Count == 0;
        var isServiceReady = status.State != "error";
        var hasPendingRebuild = _pendingRowRebuilds.Count > 0;
        var hasBusyDrive = status.Drives.Any(d => d.State is "indexing" or "pending");
        var isBusy = status.IsMaintenanceBusy || status.State is "indexing" or "loading-cache" or "pending" || hasPendingRebuild || hasBusyDrive;
        _isBusy = isBusy;
        CanRebuild = IsUserAdmin && isServiceReady && (status.State is "ready" or "idle") && !status.IsMaintenanceBusy && !hasPendingRebuild && !hasBusyDrive;
        IsDriveCheckboxEnabled = IsUserAdmin && isServiceReady && !isBusy;
        foreach (var drive in LocalDrives)
        {
            drive.CanRunRowAction = drive.RowAction == LocalDriveRowAction.Stop
                || (!isBusy && (drive.RowAction == LocalDriveRowAction.Delete || CanRebuild && drive.RowAction == LocalDriveRowAction.Rebuild));
            drive.CanEditEnabled = drive.State != TranslationManager.Instance["Local_DriveUnavailable"] && IsDriveCheckboxEnabled;
        }
        if (!isServiceReady)
        {
            IndexSummary = TranslationManager.Instance["Local_ErrorDisconnected"];
            DrivesPlaceholderText = TranslationManager.Instance["Local_ErrorPlaceholder"];
        }
        else if (LocalDrives.Count == 0)
        {
            IndexSummary = TranslationManager.Instance["Local_LoadingInfo"];
            DrivesPlaceholderText = TranslationManager.Instance["Local_LoadingPlaceholder"];
        }
        else if (isBusy)
        {
            var busyState = status.State == "indexing" ? TranslationManager.Instance["Local_StateIndexing"] : TranslationManager.Instance["Local_Rebuilding"];
            IndexSummary = string.Format(TranslationManager.Instance["Local_SummaryTemplate"], busyState, status.Drives.Count(d => d.Enabled), status.TotalFiles + status.TotalDirs);
        }
        else
            IndexSummary = string.Format(TranslationManager.Instance["Local_SummaryTemplate"], LocalDriveSettingsHelper.TranslateState(status.State), status.Drives.Count(d => d.Enabled), status.TotalFiles + status.TotalDirs);
    }

    private async void Rebuild()
    {
        if (!CanRebuild)
            return;
        SetBusy(true);
        IsLocalDrivesEmpty = false;
        IndexSummary = TranslationManager.Instance["Local_Rebuilding"];
        var enabledDrives = LocalDrives
            .Where(d => d.IsEnabled && !string.IsNullOrWhiteSpace(d.Drive))
            .Select(d => d.Drive)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var drive in LocalDrives.Where(d => enabledDrives.Contains(d.Drive)))
        {
            drive.State = TranslationManager.Instance["Local_StateIndexing"];
            drive.ItemCount = "-";
        }
        await LocalDriveRebuildHelper.RebuildEnabledDrivesAsync(
            _searchService,
            LocalDrives,
            drive => enabledDrives.Contains(drive),
            drive => _pendingRowRebuilds.Add(drive));
        _onTriggerFastRefresh?.Invoke();
    }

    private void OnLocalDriveItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(LocalDriveSettingsItem.IsEnabled) || sender is not LocalDriveSettingsItem item)
            return;

        item.CanRunRowAction = !_isBusy && item.CanRunRowAction;
    }

    private async void RunDriveAction(LocalDriveSettingsItem item)
    {
        if (_isBusy || !item.CanRunRowAction)
            return;

        if (item.RowAction == LocalDriveRowAction.Rebuild)
        {
            SetBusy(true);
            item.State = TranslationManager.Instance["Local_StateIndexing"];
            item.ItemCount = "-";
            IndexSummary = TranslationManager.Instance["Local_Rebuilding"];
            _pendingRowRebuilds.Add(item.Drive);
            _onTriggerFastRefresh?.Invoke();
            if (!await _searchService.RebuildDriveIndexAsync(item.Drive))
            {
                _pendingRowRebuilds.Remove(item.Drive);
                _observedRowRebuilds.Remove(item.Drive);
            }
        }
        else if (item.RowAction == LocalDriveRowAction.Delete)
        {
            await _searchService.DeleteDriveIndexAsync(item.Drive);
            var isUnavailable = item.State == TranslationManager.Instance["Local_DriveUnavailable"];
            item.RowAction = LocalDriveRowAction.None;
            item.State = isUnavailable ? TranslationManager.Instance["Local_DriveUnavailable"] : TranslationManager.Instance["Local_StateDisabled"];
            item.ItemCount = "-";
            item.CanRunRowAction = false;
            item.CanEditEnabled = !isUnavailable && IsDriveCheckboxEnabled;
        }
        else if (item.RowAction == LocalDriveRowAction.Stop)
        {
            // Don't touch item.State/RowAction here -- the next status poll re-derives both from whatever
            // CancelDriveRebuild actually settles the service on, mirroring the network tab's own Stop.
            _pendingRowRebuilds.Remove(item.Drive);
            _observedRowRebuilds.Remove(item.Drive);
            await _searchService.CancelDriveIndexAsync(item.Drive);
        }

        _onTriggerFastRefresh?.Invoke();
    }

    private void SetBusy(bool isBusy)
    {
        _isBusy = isBusy;
        CanRebuild = !isBusy && CanRebuild;
        IsDriveCheckboxEnabled = !isBusy && IsDriveCheckboxEnabled;
        foreach (var drive in LocalDrives)
            (drive.CanRunRowAction, drive.CanEditEnabled) = (!isBusy && drive.CanRunRowAction, !isBusy && drive.CanEditEnabled);
    }

    private void TrackPendingRebuild(UsnIndexer.DriveIndexStatus drive)
    {
        if (!_pendingRowRebuilds.Contains(drive.Drive))
            return;

        if (drive.State == "indexing")
        {
            _observedRowRebuilds.Add(drive.Drive);
        }
        else if (drive.State is "ready" or "failed" or "disabled" or "unavailable" or "cached")
        {
            _pendingRowRebuilds.Remove(drive.Drive);
            _observedRowRebuilds.Remove(drive.Drive);
        }
        else if (_observedRowRebuilds.Contains(drive.Drive))
        {
            _pendingRowRebuilds.Remove(drive.Drive);
            _observedRowRebuilds.Remove(drive.Drive);
        }
    }

    public void Cleanup() => TranslationManager.Instance.PropertyChanged -= _translationHandler;
}
