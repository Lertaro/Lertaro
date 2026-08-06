using System.Windows;
using System.Windows.Input;
using Lertaro.Core;
using Lertaro.App.Helpers;
using Lertaro.App.Services;
using MessageBox = Lertaro.App.Views.Controls.Dialogs.CustomMessageBox;
using Lertaro.App.ViewModels.Search;

using Lertaro.Core.Services.Search;

namespace Lertaro.App.ViewModels.Service;

public class SearchServiceStatusViewModel : ViewModelBase, IDisposable
{
    private readonly SearchViewModel _mainVm;
    private readonly SearchService _searchService;
    private ServiceConnectionHandler _connectionHandler = null!;
    private SearchServiceStatusPresenter _statusPresenter = null!;
    private bool _isRecovering;

    private bool _isSearchBoxEnabled;
    private Visibility _loadingPanelVisibility = Visibility.Collapsed;
    private Visibility _progressBarVisibility = Visibility.Collapsed;
    private bool _isProgressIndeterminate = true;
    private double _loadingProgress;
    private Visibility _errorIconVisibility = Visibility.Collapsed;
    private string _loadingTitle = string.Empty;
    private string _loadingStats = string.Empty;
    private Visibility _installButtonVisibility = Visibility.Collapsed;

    private bool _isServiceConnected = true;
    public bool IsServiceConnected
    {
        get => _isServiceConnected;
        set => SetProperty(ref _isServiceConnected, value);
    }

    public bool IsSearchBoxEnabled
    {
        get => _isSearchBoxEnabled;
        set => SetProperty(ref _isSearchBoxEnabled, value);
    }

    public Visibility LoadingPanelVisibility
    {
        get => _loadingPanelVisibility;
        internal set => SetProperty(ref _loadingPanelVisibility, value);
    }

    public Visibility ProgressBarVisibility
    {
        get => _progressBarVisibility;
        private set => SetProperty(ref _progressBarVisibility, value);
    }

    public bool IsProgressIndeterminate
    {
        get => _isProgressIndeterminate;
        private set => SetProperty(ref _isProgressIndeterminate, value);
    }

    public double LoadingProgress
    {
        get => _loadingProgress;
        set => SetProperty(ref _loadingProgress, value);
    }

    public Visibility ErrorIconVisibility
    {
        get => _errorIconVisibility;
        set => SetProperty(ref _errorIconVisibility, value);
    }

    public string LoadingTitle
    {
        get => _loadingTitle;
        private set => SetProperty(ref _loadingTitle, value);
    }

    public string LoadingStats
    {
        get => _loadingStats;
        private set => SetProperty(ref _loadingStats, value);
    }

    public Visibility InstallButtonVisibility
    {
        get => _installButtonVisibility;
        private set => SetProperty(ref _installButtonVisibility, value);
    }

    public ICommand InstallServiceCommand { get; private set; } = null!;

    public SearchServiceStatusViewModel(SearchViewModel mainVm, SearchService searchService)
    {
        _mainVm = mainVm;
        _searchService = searchService;

        InitializeServiceConnection();
    }

    public void CheckServiceStatusOnStartup()
    {
        _isRecovering = true;
        _statusPresenter.ShowConnecting(_connectionHandler.ShouldWaitForServiceReconnect());
        IsSearchBoxEnabled = false;
        SearchIndexBuildCoordinator.Trigger(
            _searchService,
            _connectionHandler,
            shouldWaitForReconnect: _connectionHandler.ShouldWaitForServiceReconnect,
            resetAutoInstallFlag: _connectionHandler.ResetAutoInstallFlag,
            onReadyStatus: status => _statusPresenter.ProcessStatus(status),
            onPendingStatus: status => _statusPresenter.ProcessStatus(status));
    }

    public void ResetAutoInstallFlag() => _connectionHandler.ResetAutoInstallFlag();
    public void ClearReconnectState() => _connectionHandler.ClearServiceReconnectState();

    private void OnServiceReady()
    {
        _connectionHandler.Stop();
        _connectionHandler.ClearServiceReconnectState();
        _isRecovering = false;
        _mainVm.PerformSearch(_mainVm.AdvancedQuery);
    }

    private void OnServiceReachable()
    {
        if (!_isRecovering)
            return;

        CheckServiceStatusOnStartup();
    }

    private void OnServiceInstallStarted()
    {
        _isRecovering = true;
        IsServiceConnected = false;
        LoadingTitle = TranslationManager.Instance["Service_AutoConnecting"];
        LoadingStats = TranslationManager.Instance["Service_AdminPrivilegeTip"];
        _statusPresenter.ShowReconnecting();
    }

    private void OnServiceFailedToStart()
    {
        IsServiceConnected = false;
        // Degraded Mode: Collapse loading panel so search box remains usable
        LoadingPanelVisibility = Visibility.Collapsed;
        Logger.Log("[SearchServiceStatus] Degraded Mode active: Service failed to start.");
    }

    private void InitializeServiceConnection()
    {
        _statusPresenter = new SearchServiceStatusPresenter(
            setSearchBoxEnabled: v => IsSearchBoxEnabled = v,
            setLoadingPanelVisibility: v => LoadingPanelVisibility = v,
            setProgressBarVisibility: v => ProgressBarVisibility = v,
            setProgressIndeterminate: v => IsProgressIndeterminate = v,
            setLoadingProgress: v => LoadingProgress = v,
            setErrorIconVisibility: v => ErrorIconVisibility = v,
            setLoadingTitle: v => LoadingTitle = v,
            setLoadingStats: v => LoadingStats = v,
            setInstallButtonVisibility: v => InstallButtonVisibility = v,
            onReady: OnServiceReady);

        _connectionHandler = new ServiceConnectionHandler(
            _searchService,
            onStatusUpdated: status =>
            {
                if (status.State == "ready" || status.State == "indexing" || status.State == "loading-cache")
                {
                    IsServiceConnected = true;
                }

                if (!_isRecovering && status.State == "ready")
                    return;

                _statusPresenter.ProcessStatus(status);
            },
            onServiceInstallStarted: OnServiceInstallStarted,
            onServiceInstallCompleted: CheckServiceStatusOnStartup,
            onServiceInstallError: ex => MessageBox.Show(string.Format(TranslationManager.Instance["Service_InstallFailedPrompt"], ex.Message), TranslationManager.Instance["Service_Error"], MessageBoxButton.OK, MessageBoxImage.Error),
            onServiceFailedToStart: OnServiceFailedToStart,
            onServiceReachable: () =>
            {
                IsServiceConnected = true;
                OnServiceReachable();
            }
        );

        InstallServiceCommand = new RelayCommand(_connectionHandler.ExecuteInstallService);

        IsSearchBoxEnabled = true;
    }

    public void Dispose() => _connectionHandler.Dispose();
}
