using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Flow.Launcher.Plugin;

namespace Lertaro.Plugins.FlowLauncherBridge.Engine;

/// <summary>
/// Modern host dialog window for Flow plugin settings, matching Lertaro's exact native window styling.
/// Enforces singleton instance per plugin, blocks Alt+Space/Alt+F4/Esc, and injects implicit theme styles.
/// </summary>
public sealed class FlowPluginSettingsHostWindow : Window
{
    private static readonly Dictionary<string, FlowPluginSettingsHostWindow> _activeWindows = new(StringComparer.OrdinalIgnoreCase);

    private readonly PluginPair _pair;
    private readonly FlowSettingsStorage _storage;
    private readonly string _pluginKey;
    private readonly Dictionary<string, string> _initialSnapshot;
    private bool _isConfirmed;

    public static bool ShowOrActivate(PluginPair pair, FlowSettingsStorage storage)
    {
        var id = pair.Metadata.ID ?? pair.Metadata.Name ?? string.Empty;
        if (Application.Current != null && !Application.Current.Dispatcher.CheckAccess())
        {
            return Application.Current.Dispatcher.Invoke(() => ShowOrActivate(pair, storage));
        }

        if (_activeWindows.TryGetValue(id, out var existing) && existing.IsLoaded)
        {
            if (existing.WindowState == WindowState.Minimized) existing.WindowState = WindowState.Normal;
            existing.Activate();
            existing.Focus();
            return true;
        }

        if (pair.Plugin is not ISettingProvider settingProvider) return false;
        var panel = settingProvider.CreateSettingPanel();
        if (panel == null || (panel is UserControl uc && uc.Content == null)) return false;

        var win = new FlowPluginSettingsHostWindow(pair, storage, panel);
        _activeWindows[id] = win;
        win.Closed += (_, _) => _activeWindows.Remove(id);
        win.Show();
        win.Activate();
        win.Focus();
        return true;
    }

    public FlowPluginSettingsHostWindow(PluginPair pair, FlowSettingsStorage storage, Control settingPanel)
    {
        _pair = pair;
        _storage = storage;
        _pluginKey = !string.IsNullOrEmpty(pair.Metadata.Name) ? pair.Metadata.Name : (pair.Metadata.ID ?? string.Empty);
        _initialSnapshot = storage.TakeSnapshot(_pluginKey);

        var settingsSuffix = PluginSdk.Services.TranslationService.Get("FlowLauncherBridge_SettingsSuffix");
        Title = string.IsNullOrEmpty(pair.Metadata.Name) ? "Lertaro" : $"{pair.Metadata.Name} - {settingsSuffix}";
        Width = 520;
        MinWidth = 480;
        MaxWidth = 560;
        MaxHeight = 650;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Brushes.Transparent;
        AllowsTransparency = true;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        ShowInTaskbar = false;
        Topmost = true;

        AttachSystemMenuBlocker();

        PreviewKeyDown += (s, e) =>
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                Close();
                return;
            }

            // Block Alt+F4 and Alt+Space keyboard shortcuts (matching PluginFieldPromptWindow)
            if ((e.Key == Key.System && (e.SystemKey == Key.F4 || e.SystemKey == Key.Space))
                || (e.Key == Key.F4 && (Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt)
                || (e.Key == Key.Space && (Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt))
            {
                e.Handled = true;
            }
        };

        FlowPluginSettingsStyleHelper.MergeAppThemeDictionaries(Resources);
        FlowPluginSettingsStyleHelper.RegisterImplicitControlStyles(Resources);
        Content = BuildWindowLayout(pair, settingPanel, settingsSuffix);

        Closed += (_, _) =>
        {
            if (!_isConfirmed)
            {
                _storage.RestoreSnapshot(_pluginKey, _initialSnapshot);
            }
        };
    }

    private void AttachSystemMenuBlocker() => SourceInitialized += (_, _) =>
                                                   {
                                                       if (PresentationSource.FromVisual(this) is HwndSource src)
                                                       {
                                                           src.AddHook((IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) =>
                                                           {
                                                               if (msg == 0x0112) // WM_SYSCOMMAND
                                                               {
                                                                   var command = (int)wParam & 0xFFF0;
                                                                   if (command == 0xF100 || command == 0xF060) // SC_KEYMENU (Alt+Space) or SC_CLOSE (Alt+F4)
                                                                       handled = true;
                                                               }
                                                               return IntPtr.Zero;
                                                           });
                                                       }
                                                   };

    private UIElement BuildWindowLayout(PluginPair pair, Control settingPanel, string settingsSuffix)
    {
        var outerBorder = new Border { CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), Margin = new Thickness(8) };
        outerBorder.SetResourceReference(Border.BackgroundProperty, "ContentBg");
        outerBorder.SetResourceReference(Border.BorderBrushProperty, "WindowBorderBrush");

        var shadowColor = TryFindResource("ShadowColor") is Color sc ? sc : Colors.Black;
        outerBorder.Effect = new DropShadowEffect { BlurRadius = 20, ShadowDepth = 2, Opacity = 0.25, Color = shadowColor };

        var clipBorder = new Border { CornerRadius = new CornerRadius(10), ClipToBounds = true };
        var rootGrid = new Grid();
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(38) });
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(52) });

        // Title Bar
        var titleBar = new Border { BorderThickness = new Thickness(0, 0, 0, 1) };
        titleBar.SetResourceReference(Border.BackgroundProperty, "HeaderBg");
        titleBar.SetResourceReference(Border.BorderBrushProperty, "BorderColor");
        titleBar.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };

        var titleGrid = new Grid();
        var titleStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        var titleText = new TextBlock { Text = $"{pair.Metadata.Name} - {settingsSuffix}", FontSize = 12.5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        titleText.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimary2");
        titleStack.Children.Add(titleText);
        titleGrid.Children.Add(titleStack);

        var closeBtn = new Button { Content = "\uE8BB", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 10, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 6, 6, 0) };
        if (TryFindResource("CloseButton") is Style closeStyle) closeBtn.Style = closeStyle;
        closeBtn.Click += (_, _) => Close();
        titleGrid.Children.Add(closeBtn);
        titleBar.Child = titleGrid;
        Grid.SetRow(titleBar, 0);
        rootGrid.Children.Add(titleBar);

        // Content Area
        var scrollViewer = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Margin = new Thickness(16, 14, 16, 14), Content = settingPanel };
        scrollViewer.SetResourceReference(TextElement.ForegroundProperty, "TextPrimary");
        Grid.SetRow(scrollViewer, 1);
        rootGrid.Children.Add(scrollViewer);

        // Bottom Action Bar
        var footerBar = new Border { BorderThickness = new Thickness(0, 1, 0, 0) };
        footerBar.SetResourceReference(Border.BackgroundProperty, "StatusBarBg");
        footerBar.SetResourceReference(Border.BorderBrushProperty, "BorderColor");

        var footerGrid = new Grid { Margin = new Thickness(12, 0, 12, 0) };
        var okText = PluginSdk.Services.TranslationService.Get("FlowLauncherBridge_Confirm");
        var okButton = new Button { Content = okText, Width = 80, Height = 28, IsDefault = true, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        if (TryFindResource("PrimarySettingsButton") is Style primaryStyle) okButton.Style = primaryStyle;
        okButton.Click += (_, _) =>
        {
            _isConfirmed = true;
            if (settingPanel.Tag is Action saveAction)
            {
                try { saveAction(); } catch { }
            }
            if (_pair.Plugin is ISavable savable)
            {
                try { savable.Save(); } catch { }
            }
            _storage.SaveAll();
            if (_pair.Plugin is IReloadable reloadable)
            {
                try { reloadable.ReloadData(); } catch { }
            }
            Close();
        };
        footerGrid.Children.Add(okButton);
        footerBar.Child = footerGrid;
        Grid.SetRow(footerBar, 2);
        rootGrid.Children.Add(footerBar);

        clipBorder.Child = rootGrid;
        outerBorder.Child = clipBorder;
        return outerBorder;
    }
}
