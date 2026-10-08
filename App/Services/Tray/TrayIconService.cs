using System.Runtime.InteropServices;
using System.Windows;
using Lertaro.App.ViewModels.Search;
using Lertaro.Core;

namespace Lertaro.App.Services.Tray;

public class TrayIconService : IDisposable
{
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool DestroyIcon(IntPtr handle);

    private readonly TrayMenuController _menu;
    private NotifyIcon? _notifyIcon;
    private IntPtr _hIcon;
    private bool _trayIconVisibleSetting = true;

    public static TrayIconService? Instance { get; private set; }

    /// <summary>
    /// Takes no window callbacks any more: the tray icon summons through AppWindowManager, the same entry
    /// point the global hotkey uses, so there is nothing for the window to hand in.
    /// </summary>
    /// <param name="viewModel">Accepted for the caller's sake and unused; see the note above.</param>
    public TrayIconService(QuickSearchViewModel viewModel)
    {
        _ = viewModel;
        _menu = new TrayMenuController(ApplyTrayIconVisible);
        InitializeNotifyIcon();
        Instance = this;
    }

    private void InitializeNotifyIcon()
    {
        _trayIconVisibleSetting = !UserSettings.Load().HideTrayIcon;
        _notifyIcon = new NotifyIcon { Text = "Lertaro", Visible = _trayIconVisibleSetting };
        UpdateTrayIcon();
        _notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                // The same summon the global hotkey performs, not a second toggle of its own.
                //
                // It used to call ToggleVisibility, which decides through DetermineToggleAction: show,
                // focus, hide, or reopen-as-full-window depending on the window's state and on the
                // "reopen as full window on repeat hotkey" setting. Clicking the tray icon therefore
                // cycled through those states -- the second click opened the full window and, because the
                // quick window was only hidden behind it rather than dismissed, the third click brought
                // the quick window back up again with the full one still open.
                //
                // treatFullWindowAsFocused, because this click has already taken the foreground away from
                // the full window: without it the visible-but-unfocused branch says "bring it to front",
                // which does nothing when it is already there. See that overload.
                Services.AppWindow.AppWindowManager.HandleSummon(treatFullWindowAsFocused: true);
            }
            else if (e.Button == MouseButtons.Right)
            {
                _menu.ShowAtMouse();
            }
        };
    }

    private void UpdateTrayIcon()
    {
        if (_notifyIcon == null) return;
        try
        {
            var icon = TrayIconRenderer.CreateIcon(out var newHIcon);
            if (icon == null) return;
            var oldHIcon = _hIcon;
            _hIcon = newHIcon;
            _notifyIcon.Icon = icon;
            if (oldHIcon != IntPtr.Zero)
                DestroyIcon(oldHIcon);
        }
        catch (Exception ex)
        {
            Logger.Log($"[TrayIconService] Failed to update tray icon: {ex.Message}", LogLevel.Error);
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
            // Re-add through the one rule that owns tray visibility: setting Visible = true here ignored
            // the user's "hide tray icon" choice, so the icon came back every time explorer.exe restarted.
            ApplyTrayIconVisible();
        }
        catch (Exception ex)
        {
            Logger.Log($"[TrayIconService] Failed to re-add tray icon after TaskbarCreated: {ex.Message}", LogLevel.Error);
        }
    }

    public void Dispose()
    {
        _menu.Dispose();
        App.CloseAllManagedWindows();
        if (_notifyIcon != null) { _notifyIcon.Visible = false; _notifyIcon.Dispose(); _notifyIcon = null; }
        if (_hIcon != IntPtr.Zero) { DestroyIcon(_hIcon); _hIcon = IntPtr.Zero; }
        if (Instance == this) Instance = null;
    }
}
