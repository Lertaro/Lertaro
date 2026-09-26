using System.Collections.ObjectModel;
using System.Net;
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
    private int _port;
    private bool _enableHttps;
    private bool _createChecksums;
    private bool _verifyChecksums;
    private string _receivePin = string.Empty;

    public LocalSendSettingsViewModel(UserSettings userSettings)
    {
        _userSettings = userSettings;

        _enabled = userSettings.LocalSend.Enabled;
        _deviceAlias = userSettings.LocalSend.DeviceAlias;
        _discoveryTimeout = userSettings.LocalSend.DiscoveryTimeout > 0 ? userSettings.LocalSend.DiscoveryTimeout : 1000;
        _quickSave = userSettings.LocalSend.QuickSave;
        _port = userSettings.LocalSend.Port;
        _downloadDirectory = string.IsNullOrEmpty(userSettings.LocalSend.DownloadDirectory)
            ? LocalSendSettingsModel.DefaultDownloadDirectory
            : userSettings.LocalSend.DownloadDirectory;
        _enableHttps = userSettings.LocalSend.EnableHttps;
        _createChecksums = userSettings.LocalSend.CreateChecksums;
        _verifyChecksums = userSettings.LocalSend.VerifyChecksums;
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

    /// <summary>
    /// The port LocalSend listens on and tells peers to reach it at -- the same number for both
    /// directions, since the protocol has a sender connect to the receiver's own HTTP port. Discovery
    /// (multicast and announcement) is sent from it too, so one field covers receiving and sending.
    /// </summary>
    public int Port
    {
        get => _port;
        set => SetProperty(ref _port, value);
    }

    /// <summary>
    /// <paramref name="port"/> as the service may safely bind it: the row is a text box, so an
    /// out-of-range or half-typed value falls back to the protocol default instead of being stored --
    /// and 0 has to fall back with them, since every reader of Port takes 0 to mean "not configured"
    /// (IPEndPoint.MinPort is 0, so a range check alone would let it through).
    /// </summary>
    internal static int ValidPort(int port) =>
        port >= 1 && port <= IPEndPoint.MaxPort ? port : Core.Services.LocalSend.LocalSendDiscoveryService.DefaultPort;

    public string DownloadDirectory
    {
        get => _downloadDirectory;
        set
        {
            if (SetProperty(ref _downloadDirectory, value))
                OnPropertyChanged(nameof(DownloadDirectoryDisplay));
        }
    }

    /// <summary>
    /// What the row shows: the folder files actually land in. The stored value stays the configured one
    /// (the shell token by default) so applying the page never freezes it to today's physical path.
    /// </summary>
    public string DownloadDirectoryDisplay => Core.Services.LocalSend.LocalSendServerHelper.ResolveDownloadDirectory(_downloadDirectory);

    public bool EnableHttps
    {
        get => _enableHttps;
        set => SetProperty(ref _enableHttps, value);
    }

    public bool CreateChecksums
    {
        get => _createChecksums;
        set => SetProperty(ref _createChecksums, value);
    }

    public bool VerifyChecksums
    {
        get => _verifyChecksums;
        set => SetProperty(ref _verifyChecksums, value);
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
        _userSettings.LocalSend.Port = ValidPort(_port);
        _userSettings.LocalSend.DownloadDirectory = _downloadDirectory;
        _userSettings.LocalSend.EnableHttps = _enableHttps;
        _userSettings.LocalSend.CreateChecksums = _createChecksums;
        _userSettings.LocalSend.VerifyChecksums = _verifyChecksums;
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
            SelectedPath = Core.Services.LocalSend.LocalSendServerHelper.ResolveDownloadDirectory(DownloadDirectory)
        };

        if (dialog.ShowDialog() == DialogResult.OK)
        {
            DownloadDirectory = dialog.SelectedPath;
        }
    }

    private void RefreshDevices() => _discoveredDevices.Clear();

    public void AddDiscoveredDevice(LocalSendDeviceInfo device) => _discoveredDevices.Add(device);
}
