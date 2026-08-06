using System.Windows;
using Lertaro.App.Services;
using Lertaro.Core.Indexer.Usn;

namespace Lertaro.App.ViewModels.Service;

/// <summary>
/// Translates raw UsnIndexer status updates into ViewModel property changes,
/// keeping all service-state presentation logic out of SearchViewModel.
/// </summary>
internal sealed class SearchServiceStatusPresenter
{
    private readonly Action<bool> _setSearchBoxEnabled;
    private readonly Action<Visibility> _setLoadingPanelVisibility;
    private readonly Action<Visibility> _setProgressBarVisibility;
    private readonly Action<bool> _setProgressIndeterminate;
    private readonly Action<double> _setLoadingProgress;
    private readonly Action<Visibility> _setErrorIconVisibility;
    private readonly Action<string> _setLoadingTitle;
    private readonly Action<string> _setLoadingStats;
    private readonly Action<Visibility> _setInstallButtonVisibility;
    private readonly Action _onReady;

    public SearchServiceStatusPresenter(
        Action<bool> setSearchBoxEnabled,
        Action<Visibility> setLoadingPanelVisibility,
        Action<Visibility> setProgressBarVisibility,
        Action<bool> setProgressIndeterminate,
        Action<double> setLoadingProgress,
        Action<Visibility> setErrorIconVisibility,
        Action<string> setLoadingTitle,
        Action<string> setLoadingStats,
        Action<Visibility> setInstallButtonVisibility,
        Action onReady)
    {
        _setSearchBoxEnabled = setSearchBoxEnabled;
        _setLoadingPanelVisibility = setLoadingPanelVisibility;
        _setProgressBarVisibility = setProgressBarVisibility;
        _setProgressIndeterminate = setProgressIndeterminate;
        _setLoadingProgress = setLoadingProgress;
        _setErrorIconVisibility = setErrorIconVisibility;
        _setLoadingTitle = setLoadingTitle;
        _setLoadingStats = setLoadingStats;
        _setInstallButtonVisibility = setInstallButtonVisibility;
        _onReady = onReady;
    }

    public void ShowReconnecting() => ShowLoadingState(
            title: TranslationManager.Instance["Service_WaitingStart"],
            stats: TranslationManager.Instance["Service_StartedDetail"],
            indeterminate: true);

    public void ShowConnecting(bool waitingForReconnect)
    {
        _setLoadingPanelVisibility(Visibility.Visible);
        _setProgressBarVisibility(Visibility.Visible);
        _setProgressIndeterminate(true);
        _setInstallButtonVisibility(Visibility.Collapsed);
        _setErrorIconVisibility(Visibility.Collapsed);
        if (waitingForReconnect)
        {
            _setLoadingTitle(TranslationManager.Instance["Service_WaitingStart"]);
            _setLoadingStats(TranslationManager.Instance["Service_StartedDetail"]);
        }
        else
        {
            _setLoadingTitle(TranslationManager.Instance["Service_ConnectingState"]);
            _setLoadingStats(TranslationManager.Instance["Service_ConnectingDetail"]);
        }
        _setSearchBoxEnabled(false);
    }

    public void ProcessStatus(UsnIndexer.IndexerStatus status)
    {
        if (status.State == "reconnecting")
        {
            ShowReconnecting();
            return;
        }

        if (status.State == "loading-cache")
        {
            ShowLoadingState(
                title: TranslationManager.Instance["Service_LoadingDiskIndex"],
                stats: TranslationManager.Instance["Service_LoadingDiskIndexDetail"],
                indeterminate: true);
        }
        else if (status.State == "indexing")
        {
            _setLoadingPanelVisibility(Visibility.Visible);
            _setProgressBarVisibility(Visibility.Visible);
            _setProgressIndeterminate(false);
            _setLoadingProgress(status.Progress);
            _setInstallButtonVisibility(Visibility.Collapsed);
            _setErrorIconVisibility(Visibility.Collapsed);
            _setLoadingTitle(string.Format(TranslationManager.Instance["Service_ProgressIndexing"], status.Progress));
            _setLoadingStats(string.Format(TranslationManager.Instance["Service_StatsTemplate"], status.TotalFiles, status.TotalDirs));
            _setSearchBoxEnabled(false);
        }
        else if (status.State == "ready")
        {
            _setLoadingPanelVisibility(Visibility.Collapsed);
            _setSearchBoxEnabled(true);
            _onReady();
        }
    }

    private void ShowLoadingState(string title, string stats, bool indeterminate)
    {
        _setLoadingPanelVisibility(Visibility.Visible);
        _setProgressBarVisibility(Visibility.Visible);
        _setProgressIndeterminate(indeterminate);
        _setInstallButtonVisibility(Visibility.Collapsed);
        _setErrorIconVisibility(Visibility.Collapsed);
        _setLoadingTitle(title);
        _setLoadingStats(stats);
        _setSearchBoxEnabled(false);
    }
}
