using Lertaro.Core;
using Lertaro.Core.Indexer.NetworkDrive;
using Lertaro.Core.Indexer.Usn;

using Lertaro.Core.Services.Search;

using Lertaro.Core.Services.Network;
namespace Lertaro.App.ViewModels.Settings;

/// <summary>
/// Owns the "is the search service reachable, what's its current status" read path for the Settings
/// window: polls/subscribes to the USN indexer status and network-drive statuses, coalesces bursts of
/// pushes into at most one queued UI update, and exposes the latest snapshot for
/// <see cref="SettingsViewModel"/>'s own ApplyUiState to render. Kept separate from
/// <see cref="SettingsViewModel"/>'s write path (Apply(): persisting settings and deciding which index
/// rebuilds to trigger), which has nothing to do with this monitoring.
/// </summary>
internal sealed class SettingsStatusMonitor
{
    private readonly SearchService _searchService;
    private readonly Action _onStatusUpdated;
    private readonly System.Windows.Threading.DispatcherTimer _refreshTimer;
    private readonly object _subscriptionLock = new();
    private readonly CancellationTokenSource _statusSubscriptionCts = new();
    private Task? _statusSubscriptionTask;

    private UsnIndexer.IndexerStatus _latestStatus = new() { State = "error" };
    private IReadOnlyList<NetworkIndexStatus> _latestNetworkStatuses = Array.Empty<NetworkIndexStatus>();
    private MachineSettings _latestMachineSettings = new();

    // 1 while a Dispatcher.BeginInvoke(_onStatusUpdated) is queued or running, 0 otherwise -- Interlocked
    // since ScheduleApplyUiState is called from both the UI thread and background threads (the
    // status-stream callback, RefreshLists' Task.Run continuation). Coalesces bursts of status pushes (up
    // to ~10/sec while a drive is actively indexing -- see UsnIndexer.NotifyProgressChanged) into at most
    // one update in flight plus one queued, instead of one queued per push; an unthrottled queue is what
    // made Settings tab-switching feel stuck during indexing (issue #112).
    private int _uiUpdateScheduled;

    public UsnIndexer.IndexerStatus LatestStatus => _latestStatus;
    public IReadOnlyList<NetworkIndexStatus> LatestNetworkStatuses => _latestNetworkStatuses;
    public MachineSettings LatestMachineSettings => _latestMachineSettings;

    public SettingsStatusMonitor(SearchService searchService, Action onStatusUpdated)
    {
        _searchService = searchService;
        _onStatusUpdated = onStatusUpdated;

        _refreshTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(5)
        };
        _refreshTimer.Tick += (s, e) => RefreshLists();
        _refreshTimer.Start();
        UserNetworkDriveSearch.StatusesChanged += OnNetworkStatusesChanged;
        EnsureStatusSubscription();
    }

    public void Dispose()
    {
        _refreshTimer.Stop();
        _statusSubscriptionCts.Cancel();
        UserNetworkDriveSearch.StatusesChanged -= OnNetworkStatusesChanged;
    }

    public void RefreshLists() => _ = Task.Run(async () =>
    {
        MachineSettings settings;
        var isServiceReady = false;

        try
        {
            isServiceReady = await _searchService.PingAsync();
            if (isServiceReady)
            {
                settings = await _searchService.GetMachineSettingsAsync();
                _latestNetworkStatuses = _searchService.GetNetworkIndexStatuses();
            }
            else
            {
                settings = new MachineSettings();
                _latestNetworkStatuses = Array.Empty<NetworkIndexStatus>();
            }
        }
        catch
        {
            settings = new MachineSettings();
            _latestNetworkStatuses = Array.Empty<NetworkIndexStatus>();
        }

        _latestMachineSettings = settings;
        if (!isServiceReady)
            _latestStatus = new UsnIndexer.IndexerStatus { State = "error" };

        EnsureStatusSubscription();
        ScheduleApplyUiState();
    });

    private void EnsureStatusSubscription()
    {
        lock (_subscriptionLock)
        {
            if (_statusSubscriptionTask is { IsCompleted: false })
                return;

            _statusSubscriptionTask = StartStatusSubscriptionAsync(_statusSubscriptionCts.Token);
        }
    }

    private async Task StartStatusSubscriptionAsync(CancellationToken token)
    {
        if (token.IsCancellationRequested)
            return;

        try
        {
            await SearchStatusStream.SubscribeAsync(status =>
            {
                _latestStatus = status;
                ScheduleApplyUiState();
            }, token).ConfigureAwait(false);
        }
        catch
        {
        }
    }

    private void OnNetworkStatusesChanged(IReadOnlyList<NetworkIndexStatus> statuses)
    {
        _latestNetworkStatuses = statuses;
        ScheduleApplyUiState();
    }

    private void ScheduleApplyUiState()
    {
        if (Interlocked.CompareExchange(ref _uiUpdateScheduled, 1, 0) != 0)
            return; // already scheduled/running -- it will pick up the latest state fields once it runs

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher == null)
            return;
        _ = dispatcher.BeginInvoke(new Action(() =>
        {
            try { _onStatusUpdated(); }
            finally { Interlocked.Exchange(ref _uiUpdateScheduled, 0); }
        }));
    }
}
