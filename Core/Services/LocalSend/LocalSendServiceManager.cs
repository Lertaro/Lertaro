namespace Lertaro.Core.Services.LocalSend;
using Lertaro.Core.Services.LocalSend.Models;

public sealed class LocalSendServiceManager : IDisposable
{
    private readonly object _fileSendClientLock = new();
    private LocalSendDiscoveryService? _discoveryService;
    private LocalSendServer? _server;
    private LocalSendClient? _fileSendClient;
    private bool _createChecksums = true;

    public static LocalSendServiceManager Instance { get; } = new();

    public bool IsRunning => _server != null || _discoveryService != null;
    public LocalSendDiscoveryService? DiscoveryService => _discoveryService;
    public Func<bool>? WindowOpenCheck { get; set; }
    public bool IsWindowOpen => WindowOpenCheck?.Invoke() == true;
    public bool HasRetryableFileSend => _fileSendClient?.HasRetryableFileSend == true;

    /// <summary>Raised (on a thread-pool thread) when a file has been fully received and saved to disk.</summary>
    public event EventHandler<(string FileId, string Path)>? FileReceived;

    /// <summary>Raised (on a thread-pool thread) when file transfer progress updates.</summary>
    public event EventHandler<LocalSendProgressArgs>? ProgressChanged;

    /// <summary>Raised (on a thread-pool thread) when a session is canceled.</summary>
    public event EventHandler<string>? SessionCanceled;

    /// <summary>Raised (on a thread-pool thread) when an upload request requires user confirmation.</summary>
    public event EventHandler<LocalSendUploadRequestArgs>? UploadRequested;

    /// <summary>Raised when a request to open the LocalSend send UI is initiated.</summary>
    public event EventHandler<(IReadOnlyList<string>? Files, string? Text)>? SendRequested;

    public void OpenSendWindow(IReadOnlyList<string>? files, string? text) => SendRequested?.Invoke(this, (files, text));

    public Task DiscoverDevicesAsync() => _discoveryService?.DiscoverDevicesAsync() ?? Task.CompletedTask;

    public Task ValidateDiscoveredDevicesAsync() => _discoveryService?.ValidateDiscoveredDevicesAsync() ?? Task.CompletedTask;

    public void ApplySettings(UserSettings userSettings)
    {
        var settings = userSettings.LocalSend;
        var previousFingerprint = settings.DeviceFingerprint;
        using var identityCertificate = LocalSendCertificate.LoadOrCreate();
        settings.DeviceFingerprint = LocalSendCertificate.GetFingerprint(identityCertificate);
        var generatedShowToken = string.IsNullOrWhiteSpace(settings.ShowToken);
        if (generatedShowToken)
            settings.ShowToken = Guid.NewGuid().ToString("N");
        if (previousFingerprint != settings.DeviceFingerprint || generatedShowToken)
            userSettings.Save();

        if (settings.Enabled)
        {
            Start(settings);
        }
        else
        {
            Stop();
        }
    }

    public void Start(LocalSendSettingsModel settings)
    {
        Stop();
        _createChecksums = settings.CreateChecksums;

        var alias = string.IsNullOrWhiteSpace(settings.DeviceAlias) ? Environment.MachineName : settings.DeviceAlias;
        var downloadDir = string.IsNullOrWhiteSpace(settings.DownloadDirectory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")
            : settings.DownloadDirectory;

        var identityCertificate = LocalSendCertificate.LoadOrCreate();
        settings.DeviceFingerprint = LocalSendCertificate.GetFingerprint(identityCertificate);
        var deviceInfo = LocalSendIdentity.CreateDeviceInfo(settings, alias);
        _server = new LocalSendServer { DeviceInfo = deviceInfo, IdentityCertificate = identityCertificate };
        _server.Certificate = settings.EnableHttps ? identityCertificate : null;
        _server.DownloadDirectory = downloadDir;
        _server.QuickSave = settings.QuickSave;
        _server.ReceivePin = settings.ReceivePin;
        _server.ShowToken = settings.ShowToken;
        _server.VerifyChecksums = settings.VerifyChecksums;
        _server.Start(settings.Port > 0 ? settings.Port : 53317);

        _discoveryService = new LocalSendDiscoveryService { LocalInfo = deviceInfo };
        _discoveryService.DiscoveryTimeout = settings.DiscoveryTimeout > 0 ? settings.DiscoveryTimeout : 500;
        _discoveryService.LocalInfo.Port = _server.ActualPort > 0 ? _server.ActualPort : 53317;
        _server.DeviceRegistered += (s, device) => _discoveryService?.AddDiscoveredDevice(device);
        _server.FileReceived += (s, e) => FileReceived?.Invoke(this, e);
        _server.ProgressChanged += (s, e) => ProgressChanged?.Invoke(this, e);
        _server.SessionCanceled += (s, e) => SessionCanceled?.Invoke(this, e);
        _server.UploadRequested += (s, e) => UploadRequested?.Invoke(this, e);
        _server.ShowRequested += (s, files) => OpenSendWindow(files, null);
        _discoveryService.Start(_server.ActualPort > 0 ? _server.ActualPort : 53317);
    }

    public void CancelSession(string sessionId, bool notifySender = false) => _server?.CancelSession(sessionId, notifySender);
    public void UnregisterSession(string sessionId) => _server?.UnregisterSession(sessionId);
    public bool IsSessionCanceled(string sessionId) => _server?.IsSessionCanceled(sessionId) == true;

    public void Stop()
    {
        LocalSendClient? fileSendClient;
        lock (_fileSendClientLock)
        {
            fileSendClient = _fileSendClient;
            _fileSendClient = null;
        }
        fileSendClient?.Dispose();
        _discoveryService?.Stop();
        _discoveryService?.Dispose();
        _discoveryService = null;

        _server?.Stop();
        _server?.Dispose();
        _server = null;
    }

    public async Task<(LocalSendSendResult Result, string? ErrorDetails)> SendFilesAsync(
        LocalSendDeviceInfo targetDevice, IReadOnlyList<string> filePaths, string? pin = null,
        Action<LocalSendSendProgressArgs>? onProgress = null, Action<LocalSendFileConfirmationArgs>? onFileConfirmed = null,
        CancellationToken token = default)
    {
        LocalSendClient client;
        lock (_fileSendClientLock)
        {
            _fileSendClient?.Dispose();
            client = new LocalSendClient(_server, targetDevice.Https ? targetDevice.Fingerprint : null, _createChecksums);
            _fileSendClient = client;
        }
        var senderInfo = _server?.DeviceInfo ?? new LocalSendDeviceInfo { Alias = Environment.MachineName };
        var res = await client.SendFilesAsync(targetDevice.IpAddress, targetDevice.Port, targetDevice.Https, senderInfo, filePaths, pin, onProgress, onFileConfirmed, token, targetDevice.Version).ConfigureAwait(false);
        return (res, client.LastError);
    }

    public async Task<(LocalSendSendResult Result, string? ErrorDetails)> RetryLastFailedFileAsync(
        Action<LocalSendSendProgressArgs>? onProgress = null, Action<LocalSendFileConfirmationArgs>? onFileConfirmed = null,
        CancellationToken token = default)
    {
        LocalSendClient? client;
        lock (_fileSendClientLock)
            client = _fileSendClient;
        if (client == null)
            return (LocalSendSendResult.Error, null);
        var result = await client.RetryLastFailedFileAsync(onProgress, onFileConfirmed, token).ConfigureAwait(false);
        return (result, client.LastError);
    }

    public async Task<(LocalSendSendResult Result, string? ErrorDetails)> SendTextAsync(
        LocalSendDeviceInfo targetDevice, string text, string? pin = null, CancellationToken token = default)
    {
        using var client = new LocalSendClient(_server, targetDevice.Https ? targetDevice.Fingerprint : null, _createChecksums);
        var senderInfo = _server?.DeviceInfo ?? new LocalSendDeviceInfo { Alias = Environment.MachineName };
        var res = await client.SendTextAsync(targetDevice.IpAddress, targetDevice.Port, targetDevice.Https, senderInfo, text, pin, token, targetDevice.Version).ConfigureAwait(false);
        return (res, client.LastError);
    }

    public void Dispose() => Stop();
}
