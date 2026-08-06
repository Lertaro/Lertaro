using System.Windows;
using System.Runtime.InteropServices;
using Lertaro.App.ViewModels.Search;
using Lertaro.Core;
using Application = System.Windows.Application;

using Lertaro.App.Services.Theme;
namespace Lertaro.App.Services.Tray;

public class TrayIconService : IDisposable
{
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool DestroyIcon(IntPtr handle);

    private NotifyIcon? _notifyIcon;
    private readonly QuickSearchViewModel _viewModel;
    private readonly Action _showWindowAction;
    private readonly Action _toggleVisibilityAction;
    private IntPtr _hIcon = IntPtr.Zero;

    /// <summary>The active tray service, available after the Quick window initializes it.</summary>
    public static TrayIconService? Instance { get; private set; }

    private System.Windows.Controls.ContextMenu? _wpfContextMenu;
    private System.Windows.Controls.MenuItem? _wpfItemShowWindow;
    private System.Windows.Controls.MenuItem? _wpfItemSendToOtherDevices;
    private System.Windows.Controls.MenuItem? _wpfItemToggleHotkeys;
    private System.Windows.Controls.MenuItem? _wpfItemSettings;
    private System.Windows.Controls.MenuItem? _wpfItemAbout;
    private System.Windows.Controls.MenuItem? _wpfItemCleanExit;
    private System.Windows.Controls.MenuItem? _wpfItemExit;
    private Window? _dummyWindow;
    private bool _isHotkeysDisabled;
    private bool _trayIconVisibleSetting = true;
    private Action? _pendingShowWindowOverride;

    public TrayIconService(QuickSearchViewModel viewModel, Action showWindowAction, Action toggleVisibilityAction)
    {
        _viewModel = viewModel;
        _showWindowAction = showWindowAction;
        _toggleVisibilityAction = toggleVisibilityAction;
        InitializeNotifyIcon();

        ThemeManager.Instance.ThemeChanged += UpdateTrayIconThemeColor;
        TranslationManager.Instance.PropertyChanged += OnLanguageChanged;
        UpdateMenuTexts();

        Instance = this;
    }

    private void OnLanguageChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => UpdateMenuTexts();

    private void InitializeNotifyIcon()
    {
        _trayIconVisibleSetting = !UserSettings.Load().HideTrayIcon;
        _notifyIcon = new NotifyIcon
        {
            Text = "Lertaro",
            Visible = _trayIconVisibleSetting
        };

        UpdateTrayIconThemeColor();

        _notifyIcon.MouseClick += (s, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                _toggleVisibilityAction();
            }
            else if (e.Button == MouseButtons.Right)
            {
                ShowWpfContextMenu();
            }
        };
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
            {
                DestroyIcon(oldHIcon);
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"[TrayIconService] Failed to update tray icon theme color: {ex.Message}", LogLevel.Error);
        }
    }

    private void InitializeWpfContextMenu()
    {
        _wpfContextMenu = new System.Windows.Controls.ContextMenu();

        // All static items share one neutral icon color (MenuText, matching the label text) instead
        // of an arbitrary per-item mix of AccentBlue/MenuText that had no actual meaning behind which
        // items got colored. ToggleHotkeys below is the one deliberate exception: its icon color is a
        // real state indicator (disabled = flagged in accent, enabled = neutral), not decoration.
        _wpfItemShowWindow = new System.Windows.Controls.MenuItem
        {
            Icon = CreateIcon("\uE721", "MenuText")
        };
        // ShowMenuAt's caller (the Quick window's menu button) can supply a one-shot override here --
        // e.g. to open the full window carrying over whatever query is currently typed, the way the
        // old direct "open full window" button used to -- instead of the tray icon's plain reopen with
        // no query. Consumed once and cleared so a later real-tray-icon invocation (which never sets
        // an override) can't accidentally replay a stale one from an earlier menu that was dismissed
        // without this item being clicked.
        _wpfItemShowWindow.Click += (s, e) =>
        {
            var overrideAction = _pendingShowWindowOverride;
            _pendingShowWindowOverride = null;
            if (overrideAction != null) overrideAction();
            else ShowSearchWindow();
        };

        _wpfItemSendToOtherDevices = new System.Windows.Controls.MenuItem
        {
            Icon = CreateIcon("\uE709", "MenuText")
        };
        _wpfItemSendToOtherDevices.Click += (s, e) => ShowSendToOtherDevicesWindow();

        _wpfItemToggleHotkeys = new System.Windows.Controls.MenuItem();
        _wpfItemToggleHotkeys.Click += (s, e) => ToggleHotkeys();

        _wpfItemSettings = new System.Windows.Controls.MenuItem
        {
            Icon = CreateIcon("\uE713", "MenuText")
        };
        _wpfItemSettings.Click += (s, e) => ShowSettingsWindow();

        _wpfItemAbout = new System.Windows.Controls.MenuItem
        {
            Icon = CreateIcon("\uE946", "MenuText")
        };
        _wpfItemAbout.Click += (s, e) => ShowSettingsWindow("About");

        _wpfItemCleanExit = new System.Windows.Controls.MenuItem
        {
            Icon = CreateIcon("\uE74D", "MenuText")
        };
        _wpfItemCleanExit.Click += (s, e) => TrayCleanExitHelper.CleanExit();

        _wpfItemExit = new System.Windows.Controls.MenuItem
        {
            Icon = CreateIcon("\uF3B1", "MenuText")
        };
        _wpfItemExit.Click += (s, e) => Application.Current.Shutdown();

        _wpfContextMenu.Items.Add(_wpfItemShowWindow);
        _wpfContextMenu.Items.Add(_wpfItemSendToOtherDevices);
        _wpfContextMenu.Items.Add(_wpfItemToggleHotkeys);
        _wpfContextMenu.Items.Add(_wpfItemSettings);
        _wpfContextMenu.Items.Add(new System.Windows.Controls.Separator());
        _wpfContextMenu.Items.Add(_wpfItemAbout);
        _wpfContextMenu.Items.Add(new System.Windows.Controls.Separator());
        _wpfContextMenu.Items.Add(_wpfItemCleanExit);
        _wpfContextMenu.Items.Add(_wpfItemExit);

        UpdateMenuTexts();
    }

    private static UIElement CreateIcon(string glyph, string resourceKey)
    {
        var tb = new System.Windows.Controls.TextBlock
        {
            Text = glyph,
            FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"),
            FontSize = 14,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        tb.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, resourceKey);
        return tb;
    }

    private void ShowWpfContextMenu()
    {
        // The tray icon is a WinForms NotifyIcon, not a WPF element, and at click time the Quick
        // window it belongs to may well be Hidden -- neither can serve as a PlacementTarget for
        // screen-coordinate placement, hence this throwaway 1x1 transparent window purely to anchor
        // the menu at the mouse. See ShowMenuAt for the button case, which needs none of this.
        EnsureMenuInitialized();
        _pendingShowWindowOverride = null; // this path never has a query to carry -- see ShowMenuAt

        if (_dummyWindow != null)
        {
            try { _dummyWindow.Close(); } catch { }
            _dummyWindow = null;
        }

        _dummyWindow = new Window
        {
            Width = 1,
            Height = 1,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = System.Windows.Media.Brushes.Transparent,
            ShowInTaskbar = false,
            Topmost = true
        };

        _dummyWindow.Show();
        _dummyWindow.Activate();

        _wpfContextMenu!.PlacementTarget = _dummyWindow;
        _wpfContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;

        RoutedEventHandler? closedHandler = null;
        closedHandler = (s, e) =>
        {
            _wpfContextMenu.Closed -= closedHandler;
            try
            {
                _dummyWindow?.Close();
            }
            catch { }
            _dummyWindow = null;
        };
        _wpfContextMenu.Closed += closedHandler;

        _wpfContextMenu.IsOpen = true;
    }

    // The Quick window's own search box logo calls this directly with a live element as the target --
    // unlike ShowWpfContextMenu above it needs no dummy window: WPF placement works against any live
    // element. onShowWindow, if given, overrides just this one upcoming "Show Main Window" click (see
    // _pendingShowWindowOverride above).
    public void ShowMenuAt(UIElement target, Action? onShowWindow = null)
    {
        EnsureMenuInitialized();
        _pendingShowWindowOverride = onShowWindow;
        _wpfContextMenu!.PlacementTarget = target;
        _wpfContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        _wpfContextMenu.IsOpen = true;
    }

    private void EnsureMenuInitialized()
    {
        if (_wpfContextMenu == null)
        {
            InitializeWpfContextMenu();
        }
        UpdateCleanExitVisibility();

        if (_wpfItemSendToOtherDevices != null)
        {
            var isLocalSendEnabled = UserSettings.Load().LocalSend.Enabled;
            _wpfItemSendToOtherDevices.Visibility = isLocalSendEnabled ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    // Applies a live change to the "hide tray icon" setting: toggles the actual NotifyIcon and hands
    // control of the (now sole, or now redundant) menu entry point back to the caller-supplied button.
    // While hotkeys are temporarily disabled the icon is forced visible regardless -- see ToggleHotkeys.
    public void SetTrayIconVisible(bool visible)
    {
        _trayIconVisibleSetting = visible;
        ApplyTrayIconVisible();
    }

    private void ApplyTrayIconVisible() => _notifyIcon?.Visible = _trayIconVisibleSetting || _isHotkeysDisabled;

    private void UpdateMenuTexts()
    {
        _wpfItemShowWindow?.Header = TranslationManager.Instance["Tray_ShowWindow"];
        _wpfItemSendToOtherDevices?.Header = TranslationManager.Instance["Tray_SendToOtherDevices"];
        _wpfItemSettings?.Header = TranslationManager.Instance["Tray_Settings"];
        _wpfItemAbout?.Header = TranslationManager.Instance["Tray_About"];
        _wpfItemCleanExit?.Header = TranslationManager.Instance["Tray_CleanExit"];
        _wpfItemExit?.Header = TranslationManager.Instance["Tray_Exit"];
        UpdateHotkeysMenuState();
    }

    private static void ShowSendToOtherDevicesWindow()
    {
        try
        {
            Helpers.LocalSend.LocalSendAppEventHandler.OpenSendWindow();
        }
        catch (Exception ex)
        {
            Logger.Log($"[TrayIconService] Failed to show LocalSend send window: {ex.Message}", LogLevel.Error);
        }
    }

    private void ToggleHotkeys()
    {
        if (_wpfItemToggleHotkeys == null) return;
        _isHotkeysDisabled = !_isHotkeysDisabled;
        App.HookClient?.IsHotkeysDisabled = _isHotkeysDisabled;
        UpdateHotkeysMenuState();

        // With hotkeys disabled, the hotkey can no longer summon the Quick window -- if "hide tray
        // icon" is also on, the user would have no way back into the app at all. Force the tray icon
        // visible for as long as hotkeys stay disabled, then fall back to the actual setting.
        ApplyTrayIconVisible();
    }

    private void UpdateHotkeysMenuState()
    {
        if (_wpfItemToggleHotkeys == null) return;
        _wpfItemToggleHotkeys.Header = TranslationManager.Instance["Tray_ToggleHotkeys"];
        var isDisabled = App.HookClient != null ? App.HookClient.IsHotkeysDisabled : _isHotkeysDisabled;
        if (isDisabled)
        {
            _wpfItemToggleHotkeys.Icon = CreateIcon("\uE73E", "AccentBlue");
        }
        else
        {
            _wpfItemToggleHotkeys.Icon = CreateIcon("\uE71A", "MenuText");
        }
    }

    private void ShowSettingsWindow(string? targetSection = null) => App.ShowSettingsWindow(targetSection);
    private void ShowSearchWindow() => App.ShowSearchWindow();

    // Explorer restarting (crash, or a shell update) silently wipes every previously-registered tray
    // icon; Windows broadcasts WM_TASKBARCREATED so still-running apps know to re-add theirs. Toggling
    // Visible off then on re-issues the underlying Shell_NotifyIcon add call.
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

    private void UpdateCleanExitVisibility() => _wpfItemCleanExit?.Visibility = TrayCleanExitHelper.IsOnlyAppProcessRunning() ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>
    /// Shows a balloon tip notification from the system tray icon.
    /// <paramref name="onClick"/> is invoked on the UI thread when the user clicks the balloon.
    /// </summary>
    public void ShowBalloonTip(string title, string text, ToolTipIcon icon = ToolTipIcon.Info, Action? onClick = null)
    {
        if (_notifyIcon == null) return;

        // Force icon visible for the duration of the balloon even if HideTrayIcon is on;
        // Windows will not show the balloon at all if the icon is hidden.
        _notifyIcon.Visible = true;

        if (onClick != null)
        {
            EventHandler balloonClicked = null!;
            balloonClicked = (s, e) =>
            {
                _notifyIcon.BalloonTipClicked -= balloonClicked;
                onClick();
            };
            _notifyIcon.BalloonTipClicked += balloonClicked;
        }

        _notifyIcon.ShowBalloonTip(5000, title, text, icon);

        // Restore the configured visibility after the balloon has shown.
        ApplyTrayIconVisible();
    }

    public void Dispose()
    {
        ThemeManager.Instance.ThemeChanged -= UpdateTrayIconThemeColor;
        TranslationManager.Instance.PropertyChanged -= OnLanguageChanged;
        App.CloseAllManagedWindows();

        if (_dummyWindow != null) { try { _dummyWindow.Close(); } catch { } _dummyWindow = null; }
        if (_notifyIcon != null) { _notifyIcon.Visible = false; _notifyIcon.Dispose(); _notifyIcon = null; }
        if (_hIcon != IntPtr.Zero) { DestroyIcon(_hIcon); _hIcon = IntPtr.Zero; }

        if (Instance == this) Instance = null;
    }
}
