using Lertaro.App.Views.LocalSend;
using Lertaro.App.ViewModels.LocalSend;
using Lertaro.Core;
using Lertaro.Core.Services.LocalSend;
using Lertaro.Core.Services.LocalSend.Models;
using Application = System.Windows.Application;
using System.Windows.Threading;

namespace Lertaro.App.Helpers.LocalSend;

/// <summary>
/// Handles LocalSend background events (progress, upload requests, text messages, cancellation).
/// ponytail: Split out purely to keep App.xaml.cs under the repo's 300-line limit.
/// </summary>
public static class LocalSendAppEventHandler
{
    private static LocalSendReceiveWindow? _activeReceiveWindow;
    private static LocalSendSendWindow? _activeSendWindow;
    private static LocalSendProgressArgs? _pendingProgressArgs;
    private static bool _isProgressDispatchPending;
    private static volatile bool _isReceiveWindowOpen;
    private static volatile bool _isSendWindowOpen;
    // A multicast response has the same announcement flags as a shutdown packet, so only an active request can verify availability.
    private static readonly DispatcherTimer DeviceValidationTimer = new() { Interval = TimeSpan.FromSeconds(5) };

    public static bool IsAnyWindowOpen => _isReceiveWindowOpen || _isSendWindowOpen;

    public static void Initialize(UserSettings settings)
    {
        var manager = LocalSendServiceManager.Instance;
        manager.WindowOpenCheck = () => IsAnyWindowOpen;
        manager.ApplySettings(settings);
        manager.ProgressChanged += OnProgressChanged;
        manager.SessionCanceled += OnSessionCanceled;
        manager.UploadRequested += OnUploadRequested;
        manager.SendRequested += OnSendRequested;
        DeviceValidationTimer.Tick += (_, _) => _ = LocalSendServiceManager.Instance.ValidateDiscoveredDevicesAsync();
    }

    private static void OnProgressChanged(object? sender, LocalSendProgressArgs e)
    {
        _pendingProgressArgs = e;

        if (e.IsAllDone || e.IsFinished || e.IsFailed || !_isProgressDispatchPending)
        {
            _isProgressDispatchPending = true;
            Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                _isProgressDispatchPending = false;
                var argsToUpdate = _pendingProgressArgs;
                if (argsToUpdate == null) return;

                if (_activeReceiveWindow != null && _activeReceiveWindow.IsLoaded)
                {
                    _activeReceiveWindow.HandleProgressChanged(argsToUpdate);
                }
            }));
        }
    }

    private static void OnSessionCanceled(object? sender, string sessionId) => Application.Current.Dispatcher.BeginInvoke(new Action(() =>
    {
        _activeReceiveWindow?.HandleSessionCanceled(sessionId);
        _activeSendWindow?.HandleSessionCanceled(sessionId);
    }));

    private static void OnSendRequested(object? sender, (IReadOnlyList<string>? Files, string? Text) e) => OpenSendWindow(e.Files, e.Text);

    public static void OpenSendWindow(
        IEnumerable<string>? files = null,
        string? text = null,
        LocalSendSendMode? mode = null,
        bool ignoreIfOpen = false) => Application.Current.Dispatcher.BeginInvoke(new Action(() =>
    {
        if (_activeSendWindow != null && _activeSendWindow.IsLoaded)
        {
            if (ignoreIfOpen) return;
            _activeSendWindow.Activate();
            if (_activeSendWindow.WindowState == System.Windows.WindowState.Minimized)
            {
                _activeSendWindow.WindowState = System.Windows.WindowState.Normal;
            }
            if (mode == LocalSendSendMode.Text)
            {
                _activeSendWindow.ShowTextMode(text);
            }
            else if (mode == LocalSendSendMode.Items)
            {
                _activeSendWindow.ShowItemsMode(files);
            }
            else if (files != null)
            {
                _activeSendWindow.AddFiles(files);
            }
            else if (!string.IsNullOrEmpty(text))
            {
                _activeSendWindow.ShowTextMode(text);
            }
            return;
        }
        _activeSendWindow = new LocalSendSendWindow(files, text, mode);
        _isSendWindowOpen = true;
        _activeSendWindow.Closed += (_, _) =>
        {
            _activeSendWindow = null;
            _isSendWindowOpen = false;
            DeviceValidationTimer.Stop();
        };
        _activeSendWindow.Show();
        _activeSendWindow.Activate();
        DeviceValidationTimer.Start();
        _ = RefreshDevicesAsync();
    }));

    private static async Task RefreshDevicesAsync()
    {
        var manager = LocalSendServiceManager.Instance;
        await manager.ValidateDiscoveredDevicesAsync().ConfigureAwait(false);
        await manager.DiscoverDevicesAsync().ConfigureAwait(false);
    }

    private static void OnUploadRequested(object? sender, LocalSendUploadRequestArgs e) => Application.Current.Dispatcher.BeginInvoke(new Action(() =>
    {
        // A receive dialog is modal; if a second request arrives while one is already open, opening
        // another would overwrite _activeReceiveWindow and corrupt the close-handler state. Drop the
        // duplicate request; the first window remains the single owner of receive state.
        if (_isReceiveWindowOpen)
            return;

        _activeReceiveWindow = new LocalSendReceiveWindow(e);
        _isReceiveWindowOpen = true;
        _activeReceiveWindow.Closed += (_, _) => { _activeReceiveWindow = null; _isReceiveWindowOpen = false; };
        _activeReceiveWindow.ShowDialog();
    }));
}
