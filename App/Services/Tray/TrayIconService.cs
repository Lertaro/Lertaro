using System.Runtime.InteropServices;
using System.Windows;
using Lertaro.App.Services.Theme;
using Lertaro.App.ViewModels.Search;
using Lertaro.Core;
using Application = System.Windows.Application;

namespace Lertaro.App.Services.Tray;

public class TrayIconService : IDisposable
{
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool DestroyIcon(IntPtr handle);

    private readonly Action _toggleVisibilityAction;
    private readonly TrayMenuController _menu;
    private NotifyIcon? _notifyIcon;
    private IntPtr _hIcon;
    private bool _trayIconVisibleSetting = true;
    private Action? _pendingBalloonClick;

    public static TrayIconService? Instance { get; private set; }

    public TrayIconService(QuickSearchViewModel viewModel, Action showWindowAction, Action toggleVisibilityAction)
    {
        _ = viewModel;
        _ = showWindowAction;
        _toggleVisibilityAction = toggleVisibilityAction;
        _menu = new TrayMenuController(ApplyTrayIconVisible);
        InitializeNotifyIcon();
        ThemeManager.Instance.ThemeChanged += UpdateTrayIconThemeColor;
        Instance = this;
    }

    private void InitializeNotifyIcon()
    {
        _trayIconVisibleSetting = !UserSettings.Load().HideTrayIcon;
        _notifyIcon = new NotifyIcon { Text = "Lertaro", Visible = _trayIconVisibleSetting };
        UpdateTrayIconThemeColor();
        _notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
                _toggleVisibilityAction();
            else if (e.Button == MouseButtons.Right)
                _menu.ShowAtMouse();
        };
        // One persistent click/closed pair, with the current balloon's action held in a field --
        // per-notification subscriptions self-removed only when CLICKED, so a balloon that timed
        // out left its closure attached to this long-lived icon forever (and a stale closure could
        // even answer the next balloon's click).
        _notifyIcon.BalloonTipClicked += (_, _) =>
        {
            var click = Interlocked.Exchange(ref _pendingBalloonClick, null);
            click?.Invoke();
        };
        _notifyIcon.BalloonTipClosed += (_, _) => _pendingBalloonClick = null;
    }

    private void UpdateTrayIconThemeColor()
    {
        if (_notifyIcon == null) return;
        try
        {
            Color drawingColor;
            if (ThemeManager.Instance.ActiveTheme?.IsDark == true)
            {
                drawingColor = Color.White;
            }
            else
            {
                var brush = Application.Current.Resources["AccentBlue"] as System.Windows.Media.SolidColorBrush;
                var mediaColor = brush?.Color ?? System.Windows.Media.Colors.DodgerBlue;
                drawingColor = Color.FromArgb(mediaColor.A, mediaColor.R, mediaColor.G, mediaColor.B);
            }

            var icon = TrayIconRenderer.CreateThemedIcon(drawingColor, out var newHIcon);
            if (icon == null) return;
            var oldHIcon = _hIcon;
            _hIcon = newHIcon;
            _notifyIcon.Icon = icon;
            if (oldHIcon != IntPtr.Zero)
                DestroyIcon(oldHIcon);
        }
        catch (Exception ex)
        {
            Logger.Log($"[TrayIconService] Failed to update tray icon theme color: {ex.Message}", LogLevel.Error);
        }
    }

    public void ShowMenuAt(UIElement target, Action? onShowWindow = null, bool hideShowWindow = false) =>
        _menu.ShowAt(target, onShowWindow, hideShowWindow);

    public void SetTrayIconVisible(bool visible)
    {
        _trayIconVisibleSetting = visible;
        ApplyTrayIconVisible();
    }

    private void ApplyTrayIconVisible() => _notifyIcon?.Visible = _trayIconVisibleSetting || _menu.IsHotkeysDisabled;

    public void HandleTaskbarCreated()
    {
        if (_notifyIcon == null) return;
        try
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Visible = true;
        }
        catch (Exception ex)
        {
            Logger.Log($"[TrayIconService] Failed to re-add tray icon after TaskbarCreated: {ex.Message}", LogLevel.Error);
        }
    }

    public void ShowBalloonTip(string title, string text, ToolTipIcon icon = ToolTipIcon.Info, Action? onClick = null)
    {
        if (_notifyIcon == null) return;
        _notifyIcon.Visible = true;
        _pendingBalloonClick = onClick;
        _notifyIcon.ShowBalloonTip(5000, title, text, icon);
        ApplyTrayIconVisible();
    }

    public void Dispose()
    {
        ThemeManager.Instance.ThemeChanged -= UpdateTrayIconThemeColor;
        _menu.Dispose();
        App.CloseAllManagedWindows();
        if (_notifyIcon != null) { _notifyIcon.Visible = false; _notifyIcon.Dispose(); _notifyIcon = null; }
        if (_hIcon != IntPtr.Zero) { DestroyIcon(_hIcon); _hIcon = IntPtr.Zero; }
        if (Instance == this) Instance = null;
    }
}
