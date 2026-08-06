using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using Lertaro.App.Helpers;
using Lertaro.Core;
using Lertaro.Core.Services.LocalSend.Models;

namespace Lertaro.App.ViewModels.Settings.LocalSend;

public sealed class LocalSendSettingsViewModel : ViewModelBase
{
    private readonly UserSettings _userSettings;
    private readonly ObservableCollection<LocalSendDeviceInfo> _discoveredDevices = new();

    private bool _enabled;
    private string _deviceAlias;
    private int _discoveryTimeout;
    private bool _quickSave;
    private string _downloadDirectory;
    private bool _enableHttps;
    private string _receivePin = string.Empty;

    public LocalSendSettingsViewModel(UserSettings userSettings)
    {
        _userSettings = userSettings;

        _enabled = userSettings.LocalSend.Enabled;
        _deviceAlias = userSettings.LocalSend.DeviceAlias;
        _discoveryTimeout = userSettings.LocalSend.DiscoveryTimeout > 0 ? userSettings.LocalSend.DiscoveryTimeout : 1000;
        _quickSave = userSettings.LocalSend.QuickSave;
        _downloadDirectory = string.IsNullOrEmpty(userSettings.LocalSend.DownloadDirectory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")
            : userSettings.LocalSend.DownloadDirectory;
        _enableHttps = userSettings.LocalSend.EnableHttps;
        _receivePin = userSettings.LocalSend.ReceivePin ?? string.Empty;

        SelectDownloadDirectoryCommand = new RelayCommand(SelectDownloadDirectory);
        RefreshDevicesCommand = new RelayCommand(RefreshDevices);
        RandomizeAliasCommand = new RelayCommand(RandomizeAlias);

        DiscoveredDevices = new ReadOnlyObservableCollection<LocalSendDeviceInfo>(_discoveredDevices);
    }

    public bool Enabled
    {
        get => _enabled;
        set => SetProperty(ref _enabled, value);
    }

    public string DeviceAlias
    {
        get => _deviceAlias;
        set => SetProperty(ref _deviceAlias, value);
    }

    public int DiscoveryTimeout
    {
        get => _discoveryTimeout;
        set => SetProperty(ref _discoveryTimeout, value);
    }

    public bool QuickSave
    {
        get => _quickSave;
        set => SetProperty(ref _quickSave, value);
    }

    public string DownloadDirectory
    {
        get => _downloadDirectory;
        set => SetProperty(ref _downloadDirectory, value);
    }

    public bool EnableHttps
    {
        get => _enableHttps;
        set => SetProperty(ref _enableHttps, value);
    }

    public string ReceivePin
    {
        get => _receivePin;
        set => SetProperty(ref _receivePin, value);
    }

    public bool IsServiceRunning => _userSettings.LocalSend.Enabled;
    public string DeviceHashtag => Core.Services.LocalSend.LocalSendServerHelper.GetLocalDeviceHashtag();

    public void Apply()
    {
        if (string.IsNullOrWhiteSpace(_deviceAlias))
        {
            RandomizeAlias();
        }

        _userSettings.LocalSend.Enabled = _enabled;
        _userSettings.LocalSend.DeviceAlias = _deviceAlias;
        _userSettings.LocalSend.DiscoveryTimeout = _discoveryTimeout > 0 ? _discoveryTimeout : 1000;
        _userSettings.LocalSend.QuickSave = _quickSave;
        _userSettings.LocalSend.DownloadDirectory = _downloadDirectory;
        _userSettings.LocalSend.EnableHttps = _enableHttps;
        _userSettings.LocalSend.ReceivePin = _receivePin;

        OnPropertyChanged(nameof(IsServiceRunning));
    }

    public ReadOnlyObservableCollection<LocalSendDeviceInfo> DiscoveredDevices { get; }
    public ICommand SelectDownloadDirectoryCommand { get; }
    public ICommand RefreshDevicesCommand { get; }
    public ICommand RandomizeAliasCommand { get; }

    private void RandomizeAlias()
    {
        var culture = Services.TranslationManager.Instance.CurrentCulture;
        DeviceAlias = Core.Services.LocalSend.LocalSendAliasGenerator.GenerateRandomAlias(culture);
    }

    private void SelectDownloadDirectory()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Select LocalSend Download Directory",
            UseDescriptionForTitle = true,
            SelectedPath = DownloadDirectory
        };

        if (dialog.ShowDialog() == DialogResult.OK)
        {
            DownloadDirectory = dialog.SelectedPath;
        }
    }

    private void RefreshDevices() => _discoveredDevices.Clear();

    public void AddDiscoveredDevice(LocalSendDeviceInfo device) => _discoveredDevices.Add(device);
}
