using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using Lertaro.App.Helpers.Visuals;
using Lertaro.App.Services;
using Lertaro.App.Services.Theme;
using Lertaro.Core.Services.LocalSend;
using Lertaro.Core.Services.LocalSend.Models;
using System.Windows.Threading;
namespace Lertaro.App.Views.LocalSend;
public partial class LocalSendReceiveWindow : Window
{
    private readonly LocalSendUploadRequestArgs _requestArgs;
    private readonly Stopwatch _stopwatch = new();
    private long _lastBytes;
    private bool _isCompleted;
    private string? _currentSessionId;
    private string? _lastSavedPath;
    private string? _lastRootSavedPath;
    private LocalSendTransferStage _transferStage = LocalSendTransferStage.Transferring;
    private List<LocalSendReceiveFileItem> _fileItems = new();
    private string _senderAlias = string.Empty;
    private bool _isTextMessage;
    private bool _isTextUrl;
    public LocalSendReceiveWindow(LocalSendUploadRequestArgs requestArgs)
    {
        InitializeComponent();
        _requestArgs = requestArgs;
        _currentSessionId = requestArgs.SessionId;
        SystemMenuBlocker.Attach(this);
        ThemedWindowIconHelper.Apply(this);
        ThemedWindowIconHelper.Apply(TitleBarLogo, this);
        StateChanged += (_, _) => { if (WindowState == WindowState.Maximized) WindowState = WindowState.Normal; };
        TranslationManager.Instance.PropertyChanged += OnLanguageChanged;
        Closed += (_, _) => TranslationManager.Instance.PropertyChanged -= OnLanguageChanged;
        PopulateRequestData(requestArgs.Dto);
    }
    private void OnLanguageChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        var deviceLabel = TranslationManager.Instance["Settings_LocalSend_Device"];
        TxtSender.Text = $"{deviceLabel}: {_senderAlias}";
        if (_isTextMessage)
        {
            TxtWindowTitle.Text = TranslationManager.Instance["Settings_LocalSend_TextReceivedTitle"];
            BtnCopyText.Content = TranslationManager.Instance[_isTextUrl ? "Settings_LocalSend_OpenInBrowser" : "Settings_LocalSend_Copy"];
            return;
        }
        if (GridStep1Footer.Visibility == Visibility.Visible)
        {
            UpdateSummaryText();
        }
        else
        {
            BtnCloseProgress.Content = TranslationManager.Instance[_isCompleted ? "Common_Close" : "Common_Cancel"];
            var titleKey = _fileItems.Any(i => i.IsFailed) ? "Local_StateFailed" : _isCompleted
                ? "Settings_LocalSend_Completed" : _transferStage == LocalSendTransferStage.VerifyingChecksum
                    ? "Settings_LocalSend_VerifyingChecksum" : "Settings_LocalSend_Receiving";
            TxtWindowTitle.Text = TranslationManager.Instance[titleKey];
        }
        LocalSendReceiveWindowHelper.UpdateItemLanguage(_fileItems);
    }
    private void PopulateRequestData(PrepareUploadRequestDto dto)
    {
        _senderAlias = dto.Info.Alias;
        var deviceLabel = TranslationManager.Instance["Settings_LocalSend_Device"];
        TxtSender.Text = $"{deviceLabel}: {_senderAlias}";
        SenderSecureIndicator.Visibility = dto.Info.Https ? Visibility.Visible : Visibility.Collapsed;
        if (LocalSendTextMessageHelper.TryGetMessage(dto, out var textMessage))
        {
            _isTextMessage = true;
            _isTextUrl = LocalSendTextMessageHelper.TryGetHttpUrl(textMessage, out _);
            TxtWindowTitle.Text = TranslationManager.Instance["Settings_LocalSend_TextReceivedTitle"];
            TxtTextMessage.Text = textMessage;
            BtnCopyText.Content = TranslationManager.Instance[_isTextUrl ? "Settings_LocalSend_OpenInBrowser" : "Settings_LocalSend_Copy"];
            FileListBorder.Visibility = Visibility.Collapsed;
            TextMessageBorder.Visibility = Visibility.Visible;
            GridStep1Footer.Visibility = Visibility.Collapsed;
            PanelTextFooter.Visibility = Visibility.Visible;
            BtnToggleSelectAll.Visibility = Visibility.Collapsed;
            TxtSpeed.Visibility = Visibility.Collapsed;
            TxtCounter.Visibility = Visibility.Collapsed;
            _isCompleted = true;
            return;
        }
        _fileItems = dto.Files.Select(kv => new LocalSendReceiveFileItem {
            FileId = kv.Key,
            FileName = kv.Value.FileName,
            DisplayName = kv.Value.FileName,
            Size = kv.Value.Size,
            SizeText = LocalSendServerHelper.FormatBytes(kv.Value.Size)
        }).ToList();
        LstFiles.ItemsSource = _fileItems;
        LstFiles.SelectAll();
        UpdateSummaryText();
        if (_requestArgs.IsAutoAccepted)
        {
            Loaded += (_, _) => SwitchToProgressStep();
        }
    }
    private void LstFiles_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) { if (GridStep1Footer.Visibility == Visibility.Visible) UpdateSummaryText(); else LstFiles.UnselectAll(); }
    private void BtnToggleSelectAll_Click(object sender, RoutedEventArgs e) =>
        (LstFiles.SelectedItems.Count == _fileItems.Count ? (Action)LstFiles.UnselectAll : LstFiles.SelectAll)();
    private void UpdateSummaryText()
    {
        var selectedFiles = LstFiles.SelectedItems.OfType<LocalSendReceiveFileItem>().ToList();
        var totalBytes = selectedFiles.Sum(i => i.Size);
        var sizeFormatted = LocalSendServerHelper.FormatBytes(totalBytes);
        var msgFormat = TranslationManager.Instance["Settings_LocalSend_UploadRequestMsg"];
        TxtSummary.Text = string.Format(msgFormat, _senderAlias, selectedFiles.Count, sizeFormatted);
        var hasSelection = selectedFiles.Count > 0;
        BtnSaveTo.IsEnabled = hasSelection;
        BtnAcceptDefault.IsEnabled = hasSelection;
    }
    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ChangedButton == MouseButton.Left) { try { DragMove(); } catch { } } }
    protected override void OnPreviewKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Key == Key.Escape || e.SystemKey == Key.Escape)
        {
            e.Handled = true;
        }
    }
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    { if (GridStep1Footer.Visibility != Visibility.Visible && !_isCompleted) { e.Cancel = true; return; } base.OnClosing(e); }
    protected override void OnClosed(EventArgs e)
    { base.OnClosed(e); if (!string.IsNullOrEmpty(_currentSessionId)) LocalSendServiceManager.Instance.UnregisterSession(_currentSessionId); }
    private void BtnDecline_Click(object sender, RoutedEventArgs e) { _requestArgs.Respond(false); Close(); }
    private bool ApplySelectedFiles()
    { var selected = LstFiles.SelectedItems.OfType<LocalSendReceiveFileItem>().Select(i => i.FileId).ToHashSet(); if (selected.Count == 0) { BtnDecline_Click(this, new RoutedEventArgs()); return false; } _requestArgs.SelectedFileIds = selected; return true; }
    private void BtnAcceptDefault_Click(object sender, RoutedEventArgs e)
    {
        if (_isCompleted || LocalSendServiceManager.Instance.IsSessionCanceled(_currentSessionId ?? string.Empty))
        {
            ShowSenderCanceledInStep1();
            return;
        }
        if (!ApplySelectedFiles()) return;
        _requestArgs.Respond(true);
        SwitchToProgressStep();
    }
    private void BtnSaveTo_Click(object sender, RoutedEventArgs e)
    {
        var title = TranslationManager.Instance["Settings_LocalSend_UploadRequestTitle"];
        var dialog = new OpenFolderDialog { Title = title };
        if (dialog.ShowDialog(this) == true && !string.IsNullOrWhiteSpace(dialog.FolderName))
        {
            if (_isCompleted || LocalSendServiceManager.Instance.IsSessionCanceled(_currentSessionId ?? string.Empty))
            {
                ShowSenderCanceledInStep1();
                return;
            }
            if (!ApplySelectedFiles()) return;
            _requestArgs.CustomDownloadDirectory = dialog.FolderName;
            _requestArgs.Respond(true);
            SwitchToProgressStep();
        }
    }
    private void ShowSenderCanceledInStep1()
    {
        _isCompleted = true; _requestArgs.Respond(false); LstFiles.UnselectAll();
        BtnToggleSelectAll.Visibility = Visibility.Collapsed; GridStep1Footer.Visibility = Visibility.Collapsed; PanelStep2Footer.Visibility = Visibility.Visible;
        TxtSummary.Text = TranslationManager.Instance["Settings_LocalSend_SenderCanceled"]; BtnCloseProgress.Content = TranslationManager.Instance["Common_Close"];
    }
    private void SwitchToProgressStep()
    {
        var selectedItems = LstFiles.SelectedItems.OfType<LocalSendReceiveFileItem>().ToList();
        if (selectedItems.Count == 0)
        {
            _isCompleted = true;
            Close();
            return;
        }
        LstFiles.ItemsSource = selectedItems;
        LstFiles.UnselectAll(); LstFiles.ItemContainerStyle = (Style)FindResource("LocalSendProgressListBoxItemStyle");
        BtnToggleSelectAll.Visibility = Visibility.Collapsed;
        GridStep1Footer.Visibility = Visibility.Collapsed;
        PanelStep2Footer.Visibility = Visibility.Visible;
        TxtWindowTitle.Text = TranslationManager.Instance["Settings_LocalSend_Receiving"];
        _stopwatch.Start();
        ResetInactivityTimer();
    }
    private DispatcherTimer? _inactivityTimer;
    private DispatcherTimer? _autoCloseTimer;
    private void ResetInactivityTimer()
    {
        if (_isCompleted) return;
        _inactivityTimer ??= CreateInactivityTimer();
        _inactivityTimer.Stop();
        _inactivityTimer.Start();
    }
    private DispatcherTimer CreateInactivityTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        timer.Tick += (_, _) => { timer.Stop(); if (!_isCompleted) HandleSessionCanceled(_currentSessionId ?? string.Empty); };
        return timer;
    }
    private int _maxCompletedCount;
    public void HandleProgressChanged(LocalSendProgressArgs args) => Dispatcher.BeginInvoke(new Action(() =>
    {
        if (_isCompleted && !args.IsAllDone) return;
        ResetInactivityTimer();
        _currentSessionId = args.SessionId;
        _transferStage = args.Stage;
        var isAllDone = args.IsAllDone;
        if (isAllDone) { _isCompleted = true; _inactivityTimer?.Stop(); }
        UpdateFileItemsProgress(args);
        var hasError = LstFiles.Items.OfType<LocalSendReceiveFileItem>().Any(i => i.IsFailed);
        if (args.IsFailed) _inactivityTimer?.Stop();
        var realFinishedCount = isAllDone ? args.TotalFiles : LstFiles.Items.OfType<LocalSendReceiveFileItem>().Count(i => i.IsFinished);
        _maxCompletedCount = Math.Max(_maxCompletedCount, realFinishedCount);
        TxtSummary.Text = $"({_maxCompletedCount}/{args.TotalFiles})";
        var activeTitle = args.Stage == LocalSendTransferStage.VerifyingChecksum
            ? TranslationManager.Instance["Settings_LocalSend_VerifyingChecksum"]
            : $"{TranslationManager.Instance["Settings_LocalSend_Receiving"]} ({_maxCompletedCount}/{args.TotalFiles})";
        TxtWindowTitle.Text = hasError ? TranslationManager.Instance["Local_StateFailed"] : activeTitle;
        var elapsedSec = _stopwatch.Elapsed.TotalSeconds;
        var curBytes = args.SessionBytesTransferred > 0 ? args.SessionBytesTransferred : args.BytesTransferred;
        if (elapsedSec >= 0.3 || _lastBytes == 0)
        {
            var speed = elapsedSec > 0 && curBytes >= _lastBytes ? (curBytes - _lastBytes) / elapsedSec : 0;
            TxtSpeed.Text = $"{LocalSendServerHelper.FormatBytes((long)Math.Max(0, speed))}/s";
            _lastBytes = curBytes;
            _stopwatch.Restart();
        }
        if (isAllDone)
        {
            _inactivityTimer?.Stop();
            TxtWindowTitle.Text = TranslationManager.Instance[hasError ? "Local_StateFailed" : "Settings_LocalSend_Completed"];
            _lastSavedPath = args.SavedPath;
            _lastRootSavedPath = args.RootSavedPath;
            BtnCloseProgress.Content = TranslationManager.Instance["Common_Close"];
            var target = LocalSendReceiveWindowHelper.ResolveFolderTarget(_lastRootSavedPath, _lastSavedPath);
            if (!string.IsNullOrEmpty(target)) BtnOpenFolder.Visibility = Visibility.Visible;
            if (!hasError && _requestArgs.IsAutoAccepted)
            {
                _autoCloseTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
                _autoCloseTimer.Tick += AutoCloseTimer_Tick;
                _autoCloseTimer.Stop();
                _autoCloseTimer.Start();
            }
        }
    }));
    private void AutoCloseTimer_Tick(object? sender, EventArgs e)
    {
        _autoCloseTimer?.Stop();
        Close();
    }
    private void UpdateFileItemsProgress(LocalSendProgressArgs args) => PbTransfer.Value = LocalSendReceiveWindowHelper.UpdateItemProgress(LstFiles.Items, args);
    public void HandleSessionCanceled(string sessionId) => Dispatcher.BeginInvoke(new Action(() =>
    {
        _isCompleted = true; _inactivityTimer?.Stop();
        TxtWindowTitle.Text = TranslationManager.Instance["Settings_LocalSend_Canceled"];
        LocalSendReceiveWindowHelper.MarkCanceledItems(_fileItems, TranslationManager.Instance["Settings_LocalSend_Canceled"]);
        if (GridStep1Footer.Visibility == Visibility.Visible) ShowSenderCanceledInStep1();
        else { TxtSpeed.Text = TranslationManager.Instance["Settings_LocalSend_SenderCanceled"]; BtnCloseProgress.Content = TranslationManager.Instance["Common_Close"]; }
    }));
    private void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
    {
        var target = LocalSendReceiveWindowHelper.ResolveFolderTarget(_lastRootSavedPath, _lastSavedPath);
        if (!string.IsNullOrEmpty(target)) { try { Process.Start("explorer.exe", $"/select,\"{target}\""); } catch { } }
        Close();
    }
    private void BtnCloseProgress_Click(object sender, RoutedEventArgs e)
    {
        if (!_isCompleted && !string.IsNullOrEmpty(_currentSessionId))
        {
            _isCompleted = true; TxtWindowTitle.Text = TxtSpeed.Text = TranslationManager.Instance["Settings_LocalSend_Canceled"];
            BtnCloseProgress.Content = TranslationManager.Instance["Common_Close"]; LocalSendServiceManager.Instance.CancelSession(_currentSessionId, notifySender: true); return;
        }
        Close();
    }
    private void BtnCopyText_Click(object sender, RoutedEventArgs e)
    { if (_isTextUrl) { try { Process.Start(new ProcessStartInfo(TxtTextMessage.Text.Trim()) { UseShellExecute = true }); } catch { } } else System.Windows.Clipboard.SetText(TxtTextMessage.Text); Close(); }
}
