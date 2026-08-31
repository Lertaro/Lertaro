using System.Windows;
using System.Windows.Input;
using Lertaro.App.Helpers.Visuals;
using Lertaro.App.Services;
using Lertaro.App.Services.Theme;
using Lertaro.App.ViewModels.LocalSend;
using Lertaro.Core.Services.LocalSend.Models;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace Lertaro.App.Views.LocalSend;

public partial class LocalSendSendWindow : Window
{
    private readonly LocalSendSendViewModel _vm;
    private CancelSource _cancelSource;

    public LocalSendSendWindow(
        IEnumerable<string>? initialFiles = null,
        string? initialText = null,
        LocalSendSendMode? initialMode = null)
    {
        InitializeComponent();
        SystemMenuBlocker.Attach(this);
        ThemedWindowIconHelper.Apply(this);
        ThemedWindowIconHelper.Apply(TitleBarLogo, this);
        _vm = new LocalSendSendViewModel(initialFiles, initialText, initialMode);
        DataContext = _vm;
        _vm.PropertyChanged += Vm_PropertyChanged;
        StateChanged += (_, _) => { if (WindowState == WindowState.Maximized) WindowState = WindowState.Normal; };
        TranslationManager.Instance.PropertyChanged += OnLanguageChanged;
        Closed += (_, _) =>
        {
            TranslationManager.Instance.PropertyChanged -= OnLanguageChanged;
            _vm.Dispose();
        };
        UpdateStepVisibility();
    }

    public void AddFiles(IEnumerable<string> files)
    {
        if (_vm.IsSending) return;
        _vm.AddPaths(files);
        if (_vm.CollectedItems.Count > 0) _vm.ProceedToStep1();
    }

    public void ShowTextMode(string? text)
    {
        if (!_vm.IsSending) _vm.SetMode(LocalSendSendMode.Text, text, !string.IsNullOrWhiteSpace(text));
    }

    public void ShowItemsMode(IEnumerable<string>? files)
    {
        if (_vm.IsSending) return;
        _vm.SetMode(LocalSendSendMode.Items);
        if (files == null) return;
        _vm.AddPaths(files);
        if (_vm.CollectedItems.Count > 0) _vm.ProceedToStep1();
    }

    private void OnLanguageChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => UpdateStep2UiState();
    private void Vm_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(_vm.CurrentStep)) UpdateStepVisibility();
        else if (e.PropertyName is nameof(_vm.IsSending) or nameof(_vm.TransferStage)) UpdateStep2UiState();
    }

    private void UpdateStepVisibility()
    {
        CollectPanel.Visibility = _vm.CurrentStep == 0 ? Visibility.Visible : Visibility.Collapsed;
        DevicePanel.Visibility = _vm.CurrentStep == 1 ? Visibility.Visible : Visibility.Collapsed;
        ProgressPanel.Visibility = _vm.CurrentStep == 2 ? Visibility.Visible : Visibility.Collapsed;
        UpdateStep2UiState();
    }

    private void UpdateStep2UiState()
    {
        if (_vm.CurrentStep != 2) return;
        ProgressPanel.ActionText = _vm.IsSending
            ? TranslationManager.Instance["Common_Cancel"]
            : TranslationManager.Instance["Common_Close"];

        if (_vm.IsSending)
        {
            TxtWindowTitle.Text = TranslationManager.Instance[_vm.TransferStage switch
            {
                LocalSendTransferStage.CalculatingChecksum => "Settings_LocalSend_CalculatingChecksum",
                LocalSendTransferStage.WaitingForConfirmation => "Settings_LocalSend_Waiting",
                _ => "Settings_LocalSend_Sending"
            }];
            return;
        }

        TxtWindowTitle.Text = _cancelSource switch
        {
            CancelSource.Self => TranslationManager.Instance["Settings_LocalSend_Canceled"],
            CancelSource.Receiver => TranslationManager.Instance["Settings_LocalSend_ReceiverCanceled"],
            _ => LocalSendSendPresentation.GetStatus(_vm.LastSendResult, null, string.Empty)
        };
    }

    private void Panel_CloseRequested(object? sender, EventArgs e)
    {
        if (!_vm.IsSending) Close();
    }

    private void Panel_NextRequested(object? sender, EventArgs e) => UpdateStepVisibility();

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) { try { DragMove(); } catch { } }
    }

    private async void DevicePanel_SendRequested(object? sender, LocalSendDeviceSelectionEventArgs e)
    {
        if (e.Devices.Count == 0) return;
        _vm.CurrentStep = 2;
        TxtWindowTitle.Text = TranslationManager.Instance["Settings_LocalSend_Waiting"];

        EventHandler sendingStarted = (_, _) => Dispatcher.BeginInvoke(UpdateStep2UiState);
        _vm.SendingStarted += sendingStarted;
        try
        {
            await _vm.StartSendBatchAsync(e.Devices.ToList());
        }
        finally
        {
            _vm.SendingStarted -= sendingStarted;
        }
        UpdateStep2UiState();
    }

    private void ProgressPanel_ActionRequested(object? sender, EventArgs e)
    {
        if (_vm.IsSending)
        {
            _cancelSource = CancelSource.Self;
            TxtWindowTitle.Text = TranslationManager.Instance["Settings_LocalSend_Canceled"];
            _vm.CancelCommand.Execute(null);
            return;
        }

        Close();
    }

    public void HandleSessionCanceled(string sessionId) => Dispatcher.BeginInvoke(new Action(() =>
    {
        _cancelSource = CancelSource.Receiver;
        TxtWindowTitle.Text = TranslationManager.Instance["Settings_LocalSend_ReceiverCanceled"];
        _vm.CancelCommand.Execute(null);
    }));

    protected override void OnPreviewKeyDown(WpfKeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Key == Key.Escape || e.SystemKey == Key.Escape) e.Handled = true;
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (_vm.IsSending) { e.Cancel = true; return; }
        base.OnClosing(e);
    }

    private enum CancelSource { None, Self, Receiver }
}
