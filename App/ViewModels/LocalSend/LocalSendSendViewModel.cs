using System.Collections.ObjectModel;
using System.Windows.Input;
using Lertaro.App.Helpers;
using Lertaro.App.Services;
using Lertaro.Core.Services.LocalSend;
using Lertaro.Core.Services.LocalSend.Models;
namespace Lertaro.App.ViewModels.LocalSend;
public sealed class LocalSendSendViewModel : ViewModelBase, IDisposable
{
    private readonly ObservableCollection<LocalSendSendDeviceItem> _discoveredDevices = new();
    private readonly LocalSendSendProgressTracker _progressTracker = new();
    private string _textToSend = string.Empty;
    private bool _isSending;
    private int _currentStep;
    private bool _isFromAction;
    private CancellationTokenSource? _cts;
    private readonly LocalSendDiscoveryService? _discoveryService;
    public event EventHandler? SendSuccessCompleted;
    public LocalSendSendViewModel(
        IEnumerable<string>? initialFiles = null,
        string? initialText = null,
        LocalSendSendMode? initialMode = null)
    {
        var hasFiles = initialFiles != null && initialFiles.Any();
        var hasText = !string.IsNullOrEmpty(initialText);
        _isFromAction = hasFiles || hasText;
        _currentStep = _isFromAction ? 1 : 0;
        _selectedMode = initialMode == LocalSendSendMode.Text || (initialMode == null && hasText && !hasFiles) ? 1 : 0;
        TargetFiles = new ObservableCollection<string>(initialFiles ?? Array.Empty<string>());
        _textToSend = initialText ?? string.Empty;
        DiscoveredDevices = new ReadOnlyObservableCollection<LocalSendSendDeviceItem>(_discoveredDevices);
        if (hasFiles)
        {
            foreach (var f in initialFiles!)
            {
                var isDir = System.IO.Directory.Exists(f);
                CollectedItems.Add(new LocalSendCollectedItem(f, isDir));
            }
        }
        CancelCommand = new RelayCommand(ExecuteCancel);
        _discoveryService = LocalSendServiceManager.Instance.DiscoveryService;
        if (_discoveryService != null)
        {
            _discoveryService.DeviceListChanged += OnDiscoveredDevicesChanged;
            OnDiscoveredDevicesChanged(this, EventArgs.Empty);
        }
    }
    private void OnDiscoveredDevicesChanged(object? sender, EventArgs e)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher == null) return;
        dispatcher.BeginInvoke(new Action(() =>
        {
            var discovery = LocalSendServiceManager.Instance.DiscoveryService;
            if (discovery == null) return;
            LocalSendDiscoveredDeviceSynchronizer.Synchronize(_discoveredDevices, discovery.DiscoveredDevices);
        }));
    }
    public event EventHandler? SendingStarted;
    private int _selectedMode; // 0 = Files, 1 = Text
    public int SelectedMode
    {
        get => _selectedMode;
        set
        {
            if (!SetProperty(ref _selectedMode, value)) return;
            if (_selectedMode == 1) TargetFiles.Clear();
            OnPropertyChanged(nameof(IsFilesMode));
            OnPropertyChanged(nameof(IsTextMode));
            OnPropertyChanged(nameof(CanGoNextStep));
        }
    }
    public bool IsFilesMode => _selectedMode == 0;
    public bool IsTextMode => _selectedMode == 1;
    public LocalSendSendResult LastSendResult { get; internal set; } = LocalSendSendResult.Success;
    public int CurrentStep { get => _currentStep; set => SetProperty(ref _currentStep, value); }
    public bool IsFromAction => _isFromAction;
    public ObservableCollection<LocalSendCollectedItem> CollectedItems { get; } = new();
    public ObservableCollection<string> TargetFiles { get; }
    public ObservableCollection<LocalSendSendFileItem> TransferItems => _progressTracker.Items;
    public ReadOnlyObservableCollection<LocalSendSendDeviceItem> DiscoveredDevices { get; }
    public string ConfirmedCounterText => $"({_progressTracker.ConfirmedCount}/{_progressTracker.Items.Count})";
    public string TextToSend
    {
        get => _textToSend;
        set { if (SetProperty(ref _textToSend, value)) OnPropertyChanged(nameof(CanGoNextStep)); }
    }
    public bool CanGoNextStep => IsFilesMode ? CollectedItems.Count > 0 : !string.IsNullOrWhiteSpace(_textToSend);
    public void AddPaths(IEnumerable<string> paths)
    {
        SelectedMode = 0; // Auto switch to Files mode when dropping/adding files
        foreach (var p in paths)
        {
            if (string.IsNullOrEmpty(p)) continue;
            if (CollectedItems.Any(i => string.Equals(i.Path, p, StringComparison.OrdinalIgnoreCase))) continue;
            var isDir = System.IO.Directory.Exists(p);
            if (isDir || System.IO.File.Exists(p))
            {
                CollectedItems.Add(new LocalSendCollectedItem(p, isDir));
            }
        }
        OnPropertyChanged(nameof(CanGoNextStep));
    }
    public void SetMode(LocalSendSendMode mode, string? text = null, bool proceed = false)
    {
        SelectedMode = mode == LocalSendSendMode.Text ? 1 : 0;
        if (mode == LocalSendSendMode.Text) TextToSend = text ?? string.Empty;
        _isFromAction = proceed;
        CurrentStep = proceed ? 1 : 0;
        OnPropertyChanged(nameof(IsFromAction));
    }
    public void RemoveCollectedItem(LocalSendCollectedItem item)
    {
        CollectedItems.Remove(item);
        OnPropertyChanged(nameof(CanGoNextStep));
    }
    public void ProceedToStep1()
    {
        TargetFiles.Clear();
        if (IsFilesMode)
        {
            _textToSend = string.Empty;
            foreach (var item in CollectedItems)
            {
                TargetFiles.Add(item.Path);
            }
        }
        CurrentStep = 1;
    }
    public void ReturnToStep0() => CurrentStep = 0;
    public bool IsSending { get => _isSending; private set => SetProperty(ref _isSending, value); }
    private LocalSendTransferStage _transferStage = LocalSendTransferStage.Transferring;
    public LocalSendTransferStage TransferStage { get => _transferStage; private set => SetProperty(ref _transferStage, value); }
    private string _speedText = string.Empty;
    public string SpeedText { get => _speedText; private set => SetProperty(ref _speedText, value); }
    public async Task StartSendBatchAsync(List<LocalSendSendDeviceItem> selectedDevices)
    {
        if (selectedDevices == null || selectedDevices.Count == 0) return;
        IsSending = true;
        TransferStage = LocalSendTransferStage.Transferring;
        _cts = new CancellationTokenSource();
        var allSuccess = true;
        for (var dIdx = 0; dIdx < selectedDevices.Count; dIdx++)
        {
            if (_cts.IsCancellationRequested) break;
            var deviceItem = selectedDevices[dIdx];
            var devHeader = selectedDevices.Count > 1 ? $"[{dIdx + 1}/{selectedDevices.Count}] {deviceItem.Alias}: " : string.Empty;
            var res = await SendToSingleDeviceAsync(deviceItem, devHeader);
            if (res != LocalSendSendResult.Success) allSuccess = false;
        }
        IsSending = false;
        SpeedText = string.Empty;
        if (allSuccess && !_cts.IsCancellationRequested)
        {
            SendSuccessCompleted?.Invoke(this, EventArgs.Empty);
        }
    }
    private async Task<LocalSendSendResult> SendToSingleDeviceAsync(LocalSendSendDeviceItem item, string prefix)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        long lastBytes = 0;
        double currentSpeed = 0;
        try
        {
            LocalSendSendResult result;
            string? errDetails;
            var isSendingText = IsTextMode || (TargetFiles.Count == 0 && !string.IsNullOrWhiteSpace(TextToSend));
            if (isSendingText)
            {
                var textName = TranslationManager.Instance["Settings_LocalSend_Text"];
                _progressTracker.PrepareText(TextToSend, textName);
                NotifyTransferListChanged();
                (result, errDetails) = await LocalSendServiceManager.Instance.SendTextAsync(
                    item.Device, TextToSend, item.Pin, _cts?.Token ?? CancellationToken.None);
                if (result == LocalSendSendResult.Success)
                {
                    LastSendResult = LocalSendSendResult.Success;
                    _progressTracker.MarkConfirmed(new LocalSendFileConfirmationArgs("text", textName, 1, 1), TranslationManager.Instance["Settings_LocalSend_Completed"]);
                    NotifyTransferListChanged();
                }
                else
                {
                    HandleResult(result, errDetails);
                }
                return result;
            }
            else
            {
                var filesList = TargetFiles.ToList();
                _progressTracker.PrepareFiles(filesList);
                NotifyTransferListChanged();
                (result, errDetails) = await LocalSendServiceManager.Instance.SendFilesAsync(
                    item.Device, filesList, item.Pin,
                    args => System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        TransferStage = args.Stage;
                        var elapsedSec = stopwatch.Elapsed.TotalSeconds;
                        if (elapsedSec >= 0.3 || lastBytes == 0)
                        {
                            var bytesDelta = args.BytesSent - lastBytes;
                            currentSpeed = elapsedSec > 0 && bytesDelta > 0 ? bytesDelta / elapsedSec : currentSpeed;
                            lastBytes = args.BytesSent;
                            stopwatch.Restart();
                        }
                        SpeedText = currentSpeed > 0 ? $"{LocalSendSendPresentation.FormatBytes((long)currentSpeed)}/s" : string.Empty;
                        _progressTracker.UpdateProgress(args, TranslationManager.Instance["Settings_LocalSend_Waiting"]);
                        NotifyTransferListChanged();
                        SendingStarted?.Invoke(this, EventArgs.Empty);
                    })),
                    confirmation => System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (confirmation.Result == LocalSendSendResult.Success)
                            _progressTracker.MarkConfirmed(confirmation, TranslationManager.Instance["Settings_LocalSend_Completed"]);
                        else
                            _progressTracker.MarkFailed(confirmation, LocalSendSendPresentation.GetStatus(confirmation.Result, confirmation.Error, string.Empty));
                        NotifyTransferListChanged();
                    })),
                    _cts?.Token ?? CancellationToken.None);
            }
            HandleResult(result, errDetails);
            return result;
        }
        catch (OperationCanceledException)
        {
            if (_cts != null && _cts.IsCancellationRequested)
            {
                _progressTracker.MarkPending(TranslationManager.Instance["Settings_LocalSend_Canceled"]);
                NotifyTransferListChanged();
                return LocalSendSendResult.Canceled;
            }
            _progressTracker.MarkPending(TranslationManager.Instance["Settings_LocalSend_Declined"]);
            NotifyTransferListChanged();
            return LocalSendSendResult.Declined;
        }
        catch (ObjectDisposedException)
        {
            if (_cts != null && _cts.IsCancellationRequested)
            {
                _progressTracker.MarkPending(TranslationManager.Instance["Settings_LocalSend_Canceled"]);
                NotifyTransferListChanged();
                return LocalSendSendResult.Canceled;
            }
            _progressTracker.MarkPending(TranslationManager.Instance["Settings_LocalSend_Declined"]);
            NotifyTransferListChanged();
            return LocalSendSendResult.Declined;
        }
        catch (Exception)
        {
            _progressTracker.MarkPending(TranslationManager.Instance["Settings_LocalSend_ConnectionError"]);
            NotifyTransferListChanged();
            return LocalSendSendResult.Error;
        }
    }
    private void HandleResult(LocalSendSendResult result, string? errDetails)
    {
        LastSendResult = result;
        if (result != LocalSendSendResult.Success)
        {
            _progressTracker.MarkPending(LocalSendSendPresentation.GetStatus(result, errDetails, string.Empty));
            NotifyTransferListChanged();
        }
    }
    private void NotifyTransferListChanged() => OnPropertyChanged(nameof(ConfirmedCounterText));
    public ICommand CancelCommand { get; }
    private void ExecuteCancel() { try { _cts?.Cancel(); } catch (ObjectDisposedException) { } }
    public void Dispose()
    {
        _discoveryService?.DeviceListChanged -= OnDiscoveredDevicesChanged;
        try { _cts?.Cancel(); } catch (ObjectDisposedException) { }
        _cts?.Dispose();
        _cts = null;
    }
}
