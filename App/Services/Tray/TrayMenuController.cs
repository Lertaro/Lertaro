using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Lertaro.Core;
using ContextMenu = System.Windows.Controls.ContextMenu;
using MenuItem = System.Windows.Controls.MenuItem;
using Brushes = System.Windows.Media.Brushes;
using FontFamily = System.Windows.Media.FontFamily;
using HorizontalAlignment = System.Windows.HorizontalAlignment;

namespace Lertaro.App.Services.Tray;

// Split out to keep TrayIconService focused on the native icon; this owns only the WPF menu surface.
internal sealed class TrayMenuController : IDisposable
{
    private readonly Action _visibilityChanged;
    private ContextMenu? _menu;
    private MenuItem? _showWindow;
    private MenuItem? _send;
    private MenuItem? _spaceAnalyzer;
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
        CloseAnchor();
        _anchor = new Window
        {
            Width = 1,
            Height = 1,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ShowInTaskbar = false,
            Topmost = true
        };
        _anchor.Show();
        _anchor.Activate();
        _menu!.PlacementTarget = _anchor;
        _menu.Placement = PlacementMode.MousePoint;
        RoutedEventHandler? closed = null;
        closed = (_, _) =>
        {
            _menu.Closed -= closed;
            CloseAnchor();
        };
        _menu.Closed += closed;
        _menu.IsOpen = true;
    }

    public void ShowAt(UIElement target, Action? onShowWindow)
    {
        EnsureInitialized();
        _showWindowOverride = onShowWindow;
        _menu!.PlacementTarget = target;
        _menu.Placement = PlacementMode.MousePoint;
        _menu.IsOpen = true;
    }

    private void EnsureInitialized()
    {
        if (_menu == null)
            Initialize();
        _cleanExit!.Visibility = TrayCleanExitHelper.IsOnlyAppProcessRunning() ? Visibility.Visible : Visibility.Collapsed;
        _send!.Visibility = UserSettings.Load().LocalSend.Enabled ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Initialize()
    {
        _menu = new ContextMenu();
        _showWindow = Item("\uE721", () =>
        {
            var action = _showWindowOverride;
            _showWindowOverride = null;
            if (action != null) action(); else App.ShowSearchWindow();
        });
        _send = Item("\uE709", ShowSendWindow);
        _spaceAnalyzer = Item("\uE9D5", App.ShowSpaceAnalyzerWindow);
        _toggleHotkeys = Item(string.Empty, ToggleHotkeys);
        _settings = Item("\uE713", () => App.ShowSettingsWindow());
        _about = Item("\uE946", () => App.ShowSettingsWindow("About"));
        _cleanExit = Item("\uE74D", TrayCleanExitHelper.CleanExit);
        _exit = Item("\uF3B1", () => System.Windows.Application.Current.Shutdown());

        _menu.Items.Add(_showWindow);
        _menu.Items.Add(_send);
        _menu.Items.Add(_spaceAnalyzer);
        _menu.Items.Add(CreateSeparator());
        _menu.Items.Add(_toggleHotkeys);
        _menu.Items.Add(_settings);
        _menu.Items.Add(CreateSeparator());
        _menu.Items.Add(_about);
        _menu.Items.Add(CreateSeparator());
        _menu.Items.Add(_cleanExit);
        _menu.Items.Add(_exit);
        UpdateTexts();
    }

    private static MenuItem Item(string glyph, Action action)
    {
        var item = new MenuItem { Icon = glyph.Length == 0 ? null : CreateIcon(glyph, "MenuText") };
        item.Click += (_, _) => action();
        return item;
    }

    private static UIElement CreateIcon(string glyph, string resourceKey)
    {
        var icon = new TextBlock
        {
            Text = glyph,
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 14,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        icon.SetResourceReference(TextBlock.ForegroundProperty, resourceKey);
        return icon;
    }

    private static Separator CreateSeparator()
    {
        var separator = new Separator();
        separator.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "SeparatorBrush");
        return separator;
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
        _spaceAnalyzer!.Header = TranslationManager.Instance["Tray_SpaceAnalyzer"];
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

    private void CloseAnchor()
    {
        try { _anchor?.Close(); } catch { }
        _anchor = null;
    }

    public void Dispose()
    {
        TranslationManager.Instance.PropertyChanged -= OnLanguageChanged;
        CloseAnchor();
    }
}
