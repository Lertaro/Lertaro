using System.Windows;
using System.Windows.Input;
using Lertaro.App.Helpers;
using Lertaro.App.Services;
using Lertaro.Core.Indexer.Usn;
using MessageBox = Lertaro.App.Views.Controls.Dialogs.CustomMessageBox;

using Lertaro.Core.Services.Search;

namespace Lertaro.App.ViewModels.Settings;

public class ServiceSettingsViewModel : ViewModelBase
{
    private readonly SearchService _searchService;
    private readonly Action _onStatusChanged;

    private Visibility _progressBarVisibility = Visibility.Collapsed;
    private Visibility _errorIconVisibility = Visibility.Collapsed;
    private Visibility _installButtonVisibility = Visibility.Collapsed;
    private string _loadingTitle = "";
    private string _loadingStats = "";

    public ServiceSettingsViewModel(SearchService searchService, Action onStatusChanged)
    {
        _searchService = searchService;
        _onStatusChanged = onStatusChanged;
        _loadingTitle = TranslationManager.Instance["Service_GettingStatus"];
        InstallServiceCommand = new RelayCommand(InstallService);
    }

    public ICommand InstallServiceCommand { get; }

    public Visibility ProgressBarVisibility
    {
        get => _progressBarVisibility;
        set => SetProperty(ref _progressBarVisibility, value);
    }

    public Visibility ErrorIconVisibility
    {
        get => _errorIconVisibility;
        set => SetProperty(ref _errorIconVisibility, value);
    }

    public Visibility InstallButtonVisibility
    {
        get => _installButtonVisibility;
        set => SetProperty(ref _installButtonVisibility, value);
    }

    public string LoadingTitle
    {
        get => _loadingTitle;
        set => SetProperty(ref _loadingTitle, value);
    }

    public string LoadingStats
    {
        get => _loadingStats;
        set => SetProperty(ref _loadingStats, value);
    }

    public void UpdateStatus(UsnIndexer.IndexerStatus status)
    {
        if (status.State == "error")
        {
            ProgressBarVisibility = Visibility.Collapsed;
            ErrorIconVisibility = Visibility.Visible;
            InstallButtonVisibility = Visibility.Visible;
            LoadingTitle = TranslationManager.Instance["Service_ErrorTitle"];
            LoadingStats = TranslationManager.Instance["Service_ErrorStats"];
        }
        else if (status.IsMaintenanceBusy || status.State is "indexing" or "loading-cache" or "pending")
        {
            ProgressBarVisibility = Visibility.Visible;
            ErrorIconVisibility = Visibility.Collapsed;
            InstallButtonVisibility = Visibility.Collapsed;
            LoadingTitle = status.State == "indexing"
                ? string.Format(TranslationManager.Instance["Service_ProgressIndexing"], status.Progress)
                : TranslationManager.Instance["Service_ProgressLoading"];
            LoadingStats = string.Format(TranslationManager.Instance["Service_StatsTemplate"], status.TotalFiles, status.TotalDirs);
        }
        else
        {
            ProgressBarVisibility = Visibility.Collapsed;
            ErrorIconVisibility = Visibility.Collapsed;
            InstallButtonVisibility = Visibility.Collapsed;
            LoadingTitle = TranslationManager.Instance["Service_ReadyTitle"];
            LoadingStats = string.Format(TranslationManager.Instance["Service_ReadyStats"], status.TotalFiles + status.TotalDirs);
        }
    }

    private void InstallService() => Task.Run(() =>
    {
        System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
        {
            ProgressBarVisibility = Visibility.Visible;
            ErrorIconVisibility = Visibility.Collapsed;
            InstallButtonVisibility = Visibility.Collapsed;
            LoadingTitle = TranslationManager.Instance["Service_InstallingTitle"];
            LoadingStats = TranslationManager.Instance["Service_InstallingStats"];
        }));

        try
        {
            ServiceInstallManager.InstallService(
                () => System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() => _onStatusChanged?.Invoke())),
                ex => System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    MessageBox.Show($"{TranslationManager.Instance["Service_InstallFailed"]}{ex.Message}", TranslationManager.Instance["Service_Error"], MessageBoxButton.OK, MessageBoxImage.Error);
                    _onStatusChanged?.Invoke();
                }))
            );
        }
        catch (Exception ex)
        {
            System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                MessageBox.Show($"{TranslationManager.Instance["Service_Exception"]}{ex.Message}", TranslationManager.Instance["Service_Error"], MessageBoxButton.OK, MessageBoxImage.Error);
                _onStatusChanged?.Invoke();
            }));
        }
    });
}
