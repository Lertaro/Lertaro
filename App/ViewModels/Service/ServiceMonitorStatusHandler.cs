using System.Windows;
using Lertaro.App.Services;
using Lertaro.Core;
using Lertaro.Core.Indexer.Usn;
using Lertaro.App.ViewModels.Search;

namespace Lertaro.App.ViewModels.Service;

/// <summary>
/// Handles service status transitions and maps them to the ServiceMonitorViewModel
/// visibility and text properties, keeping the state-machine out of the main VM.
/// </summary>
internal sealed class ServiceMonitorStatusHandler
{
    private readonly ServiceMonitorViewModel _vm;
    private readonly QuickSearchViewModel _mainVm;
    private readonly ServiceConnectionHandler _connectionHandler;

    public ServiceMonitorStatusHandler(
        ServiceMonitorViewModel vm,
        QuickSearchViewModel mainVm,
        ServiceConnectionHandler connectionHandler)
    {
        _vm = vm;
        _mainVm = mainVm;
        _connectionHandler = connectionHandler;
    }

    public void ProcessStatusTimerTick(UsnIndexer.IndexerStatus status)
    {
        if (status.State == "reconnecting")
        {
            _vm.ShowServiceReconnectState(TranslationManager.Instance["Service_WaitingStart"], TranslationManager.Instance["Service_StartedDetail"]);
            return;
        }

        _vm.IsServiceConnected = true;
        _vm.ErrorIconVisibility = Visibility.Collapsed;

        var hasQuery = !string.IsNullOrWhiteSpace(_mainVm.Search.SearchQuery);

        if (status.State == "loading-cache")
        {
            _connectionHandler.ClearServiceReconnectState();
            if (hasQuery)
            {
                _vm.LoadingPanelVisibility = Visibility.Collapsed;
                _vm.StatusBarVisibility = Visibility.Visible;
                _vm.StatusText = string.Format(TranslationManager.Instance["Service_LoadingCacheQuery"], _mainVm.Search.Results.Count);
            }
            else
            {
                _vm.LoadingPanelVisibility = Visibility.Visible;
                _vm.ProgressBarVisibility = Visibility.Visible;
                _vm.IsProgressIndeterminate = true;
                _vm.InstallButtonVisibility = Visibility.Collapsed;
                _vm.LoadingTitle = TranslationManager.Instance["Service_LoadingDiskIndex"];
                _vm.LoadingStats = TranslationManager.Instance["Service_LoadingDiskIndexDetail"];
            }
        }
        else if (status.State == "indexing")
        {
            _connectionHandler.ClearServiceReconnectState();
            if (hasQuery)
            {
                _vm.LoadingPanelVisibility = Visibility.Collapsed;
                _vm.StatusBarVisibility = Visibility.Visible;
                _vm.StatusText = string.Format(TranslationManager.Instance["Service_BuildingIndexQuery"], status.Progress, _mainVm.Search.Results.Count);
            }
            else
            {
                _vm.LoadingPanelVisibility = Visibility.Visible;
                _vm.ProgressBarVisibility = Visibility.Visible;
                _vm.IsProgressIndeterminate = false;
                _vm.LoadingProgress = status.Progress;
                _vm.InstallButtonVisibility = Visibility.Collapsed;
                _vm.LoadingTitle = string.Format(TranslationManager.Instance["Service_ProgressIndexing"], status.Progress);
                _vm.LoadingStats = string.Format(TranslationManager.Instance["Service_StatsTemplate"], status.TotalFiles, status.TotalDirs);
            }
        }
        else if (status.State == "ready")
        {
            _connectionHandler.Stop();
            _vm.IsIndexReady = true;
            _connectionHandler.ClearServiceReconnectState();

            _vm.LoadingPanelVisibility = Visibility.Collapsed;
            _vm.ApplyReadyStatus(status);
            if (hasQuery)
                _mainVm.Search.PerformSearch(_mainVm.Search.SearchQuery);
            Logger.Log($"[ServiceMonitor] Index ready. Files={status.TotalFiles}, Dirs={status.TotalDirs}");
        }
    }
}
