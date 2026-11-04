using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Lertaro.Core;
using Lertaro.App.Helpers.Visuals;
using Lertaro.App.Services.ShellMenu.ActionFlyout;
using Lertaro.App.Services.ShellMenu.QuickNav;
using MenuItem = System.Windows.Controls.MenuItem;
using Menu = System.Windows.Controls.Menu;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Application = System.Windows.Application;
using HorizontalAlignment = System.Windows.HorizontalAlignment;

namespace Lertaro.App.Services.Tray;

// Split out to keep TrayIconService focused on the native icon; this owns only the WPF menu surface.
// The surface is an opaque Popup hosting a Menu, not a ContextMenu. WPF builds the popup inside every
// ContextMenu itself, with AllowsTransparency hardcoded to true at the moment the popup is created, in a
// private field and with no overridable factory, so re-templating the menu cannot reach it. A layered
// window cannot render subpixel antialiasing, which is why every row of the old tray menu read gray.
internal sealed class TrayMenuController : IDisposable
{
    private readonly Action _visibilityChanged;
    private Menu? _menu;
    private Popup? _popup;
    private MenuItem? _showWindow;
    private MenuItem? _send;
    private MenuItem? _toggleHotkeys;
    private MenuItem? _settings;
    private MenuItem? _about;
    private MenuItem? _cleanExit;
    private MenuItem? _exit;
    private Window? _anchor;
    private Action? _showWindowOverride;

    public TrayMenuController(Action visibilityChanged)
    {
        _visibilityChanged = visibilityChanged;
        TranslationManager.Instance.PropertyChanged += OnLanguageChanged;
    }

    public bool IsHotkeysDisabled { get; private set; }

    public void ShowAtMouse()
    {
        EnsureInitialized();
        _showWindowOverride = null;
        _showWindow!.Visibility = Visibility.Visible;
        CloseMenu();
        EnsureAnchor();
        var cursor = System.Windows.Forms.Control.MousePosition;
        WindowParking.PlaceAtPoint(_anchor!, cursor.X, cursor.Y);
        _anchor!.Activate();
        OpenMenu(_anchor);
    }

    // The anchor is a window nothing is ever drawn in: it exists only so the popup has a placement target
    // and an owner to lose activation through. It sits in the drop-shadow gap around the menu, so any pixel
    // it paints reads as a stray mark beside it. WindowStyle.None still leaves the resize frame while
    // ResizeMode stays at its CanResize default, and Win11 draws that frame as a rounded stroke with a
    // shadow, so around a 1x1 client area the anchor read as a 13x13 hollow ring on top of everything
    // (issue #468). NoResize drops the frame, but a WPF window never reports smaller than 2x2; layering it,
    // with a transparent background at zero opacity, is what leaves it with no pixels at all while it stays
    // showable and activatable, which is all this one is for.
    //
    // One anchor for the controller's whole life rather than one per showing: a fresh window each time
    // meant a new handle, render target and foreground handoff, and a new HwndWrapper class GUID in every
    // window enumerator. A closed WPF window cannot be shown again, so it is parked instead of closed.
    private void EnsureAnchor()
    {
        if (_anchor != null) return;
        // The position has to be given rather than left to the system: an unset Left/Top is CW_USEDEFAULT,
        // which cascades the window near the top-left of the screen instead of under the menu -- see
        // WindowParking.Park's own note on that. Parked, then shown, then landed: the handle a physical
        // placement needs does not exist until after Show, and nothing is painted in between.
        _anchor = new Window
        {
            Width = 1,
            Height = 1,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            AllowsTransparency = true,
            Background = System.Windows.Media.Brushes.Transparent,
            Opacity = 0,
            ShowInTaskbar = false,
            Topmost = true
        };
        WindowParking.Park(_anchor);
        _anchor.Show();
    }

    public void ShowAt(UIElement target, Action? onShowWindow, bool hideShowWindow)
    {
        EnsureInitialized();
        _showWindowOverride = onShowWindow;
        _showWindow!.Visibility = hideShowWindow ? Visibility.Collapsed : Visibility.Visible;
        CloseMenu();
        OpenMenu(target);
    }

    private void EnsureInitialized()
    {
        if (_menu == null)
            Initialize();
        _send!.Visibility = UserSettings.Load().LocalSend.Enabled ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OpenMenu(UIElement target)
    {
        var popup = _popup!;
        popup.PlacementTarget = target;
        popup.Placement = PlacementMode.MousePoint;
        popup.IsOpen = true;
        // Focus has to land inside the popup, or the arrow keys and Escape keep going to whatever window
        // the click left behind. A ContextMenu used to do this for itself the moment it opened.
        _menu!.Focus();
    }

    private void Initialize()
    {
        // The rows still take the app's implicit MenuItem style, so they read and measure like every other
        // menu; only the host changes. Menu has no Orientation, so the shared panel resource replaces its
        // horizontal item panel with a column. The popup, its chrome and the menu are built once and
        // reopened: the Border holds the Menu as its child, and rebinding that per show would try to
        // reparent an element that still belongs to the previous chrome.
        _menu = new Menu
        {
            Background = System.Windows.Media.Brushes.Transparent,
            ItemsPanel = (ItemsPanelTemplate)Application.Current.FindResource("VerticalMenuItemsPanel")
        };
        Grid.SetIsSharedSizeScope(_menu, true);
        _menu.PreviewKeyDown += OnMenuKeyDown;

        _showWindow = Item("\uE721", () =>
        {
            var action = _showWindowOverride;
            _showWindowOverride = null;
            if (action != null) action(); else App.ShowSearchWindow();
        });
        _send = Item("\uE709", ShowSendWindow);
        _toggleHotkeys = Item(string.Empty, ToggleHotkeys);
        _settings = Item("\uE713", () => App.ShowSettingsWindow());
        _about = Item("\uE946", () => App.ShowSettingsWindow("About"));
        _cleanExit = Item("\uE74D", () => TrayCleanExitHelper.CleanExit());
        _exit = Item("\uF3B1", () => Application.Current.Shutdown());

        _menu.Items.Add(_showWindow);
        _menu.Items.Add(_send);
        _menu.Items.Add(QuickNavigationMenu.CreateSeparator());
        _menu.Items.Add(_toggleHotkeys);
        _menu.Items.Add(_settings);
        _menu.Items.Add(QuickNavigationMenu.CreateSeparator());
        _menu.Items.Add(_about);
        _menu.Items.Add(QuickNavigationMenu.CreateSeparator());
        _menu.Items.Add(_cleanExit);
        _menu.Items.Add(_exit);

        var chrome = new Border
        {
            Style = (Style)Application.Current.FindResource("MenuPopupChrome"),
            Child = _menu
        };
        // StaysOpen false is what used to dismiss the ContextMenu on a click anywhere outside it, and the
        // same mouse capture does it here. No PopupAnimation: on an opaque surface a fade dims the menu
        // instead of fading it in.
        _popup = new Popup
        {
            StaysOpen = false,
            Child = chrome
        };
        _popup.Closed += (_, _) => ParkAnchor();
        UpdateTexts();
    }

    private void OnMenuKeyDown(object sender, KeyEventArgs e)
    {
        if (_popup is not { IsOpen: true } || _menu == null) return;
        var key = ActionFlyoutItems.IsHighlightedKey;
        if (key == null) return;
        // Walked here rather than through the shared GetActiveMenuState because that one recurses into an
        // open submenu this flat menu never has, and it gates only on IsEnabled: LocalSend's row is
        // Collapsed, not disabled, when the feature is off, so the shared walk would let Enter launch a
        // window the user turned off from an invisible row.
        var items = _menu.Items.OfType<MenuItem>().Where(mi => mi.IsEnabled && mi.Visibility != Visibility.Collapsed).ToList();
        var highlighted = items.FindIndex(mi => (bool)mi.GetValue(key.DependencyProperty));
        switch (e.Key)
        {
            case Key.Escape:
                e.Handled = true;
                CloseMenu();
                break;
            case Key.Down or Key.Up when items.Count > 0:
                var next = e.Key == Key.Down
                    ? highlighted < 0 ? 0 : (highlighted + 1) % items.Count
                    : highlighted <= 0 ? items.Count - 1 : highlighted - 1;
                if (highlighted >= 0) items[highlighted].SetValue(key, false);
                items[next].SetValue(key, true);
                items[next].BringIntoView();
                e.Handled = true;
                break;
            case Key.Enter or Key.Space when highlighted >= 0:
                e.Handled = true;
                // Raising the routed event is how the action flyout invokes a row too; the item's own Click
                // handler then closes the menu and runs the action.
                items[highlighted].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                break;
        }
    }

    private MenuItem Item(string glyph, Action action)
    {
        var item = new MenuItem { Icon = glyph.Length == 0 ? null : CreateIcon(glyph, "MenuText") };
        item.Click += (_, _) =>
        {
            // Close before running: a ContextMenu closed itself on a click, a Popup does not, and an item
            // that opens a window must not leave the menu sitting on top of it.
            CloseMenu();
            action();
        };
        return item;
    }

    private static UIElement CreateIcon(string glyph, string resourceKey)
    {
        var icon = new TextBlock
        {
            Text = glyph,
            FontFamily = AppTypography.IconFont,
            FontSize = 14,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        icon.SetResourceReference(TextBlock.ForegroundProperty, resourceKey);
        return icon;
    }

    private void ToggleHotkeys()
    {
        IsHotkeysDisabled = !IsHotkeysDisabled;
        App.HookClient?.IsHotkeysDisabled = IsHotkeysDisabled;
        UpdateHotkeyState();
        _visibilityChanged();
    }

    private void UpdateTexts()
    {
        if (_menu == null) return;
        _showWindow!.Header = TranslationManager.Instance["Tray_ShowWindow"];
        _send!.Header = TranslationManager.Instance["Tray_SendToOtherDevices"];
        _settings!.Header = TranslationManager.Instance["Tray_Settings"];
        _about!.Header = TranslationManager.Instance["Tray_About"];
        _cleanExit!.Header = TranslationManager.Instance["Tray_CleanExit"];
        _exit!.Header = TranslationManager.Instance["Tray_Exit"];
        UpdateHotkeyState();
    }

    private void UpdateHotkeyState()
    {
        if (_toggleHotkeys == null) return;
        _toggleHotkeys.Header = TranslationManager.Instance["Tray_ToggleHotkeys"];
        var disabled = App.HookClient?.IsHotkeysDisabled ?? IsHotkeysDisabled;
        _toggleHotkeys.Icon = CreateIcon(disabled ? "\uE73E" : "\uE71A", disabled ? "AccentBlue" : "MenuText");
    }

    private static void ShowSendWindow()
    {
        try { Helpers.LocalSend.LocalSendAppEventHandler.OpenSendWindow(); }
        catch (Exception ex) { Logger.Log($"[TrayMenuController] Failed to show LocalSend window: {ex.Message}", LogLevel.Error); }
    }

    private void OnLanguageChanged(object? sender, PropertyChangedEventArgs e) => UpdateTexts();

    private void CloseMenu()
    {
        if (_popup is { IsOpen: true } popup)
            popup.IsOpen = false;
        ParkAnchor();
    }

    private void ParkAnchor()
    {
        // Parked, not closed: EnsureAnchor keeps this one window for every showing.
        if (_anchor != null) WindowParking.Park(_anchor);
    }

    public void Dispose()
    {
        TranslationManager.Instance.PropertyChanged -= OnLanguageChanged;
        CloseMenu();
        try { _anchor?.Close(); } catch { }
        _anchor = null;
    }
}
