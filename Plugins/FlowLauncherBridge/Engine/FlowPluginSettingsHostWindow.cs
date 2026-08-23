using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
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
        if (panel == null) return false;

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
            // Block Alt+F4 and Alt+Space keyboard shortcuts (matching PluginFieldPromptWindow)
            if ((e.Key == Key.System && (e.SystemKey == Key.F4 || e.SystemKey == Key.Space))
                || (e.Key == Key.F4 && (Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt)
                || (e.Key == Key.Space && (Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt))
            {
                e.Handled = true;
            }
        };

        MergeAppThemeDictionaries();
        RegisterImplicitControlStyles();
        Content = BuildWindowLayout(pair, settingPanel, settingsSuffix);

        Closed += (_, _) =>
        {
            if (_pair.Plugin is ISavable savable)
            {
                try { savable.Save(); } catch { }
            }
            _storage.SaveAll();
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

    private void MergeAppThemeDictionaries()
    {
        try
        {
            Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/Lertaro.App;component/Resources/Styles.xaml", UriKind.Absolute) });
            Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/Lertaro.App;component/Resources/Styles/Controls/Menu.xaml", UriKind.Absolute) });
            Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/Lertaro.App;component/Resources/Styles/Windows/SearchWindow.xaml", UriKind.Absolute) });
            Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/Lertaro.App;component/Resources/Styles/Windows/SettingsWindow.xaml", UriKind.Absolute) });
        }
        catch { }

        MapFlowResource("ItemTitleColor", "TextPrimary");
        MapFlowResource("ItemSubTitleColor", "TextSecondary");
        MapFlowResource("WindowBackground", "ContentBg");
        MapFlowResource("BackgroundColor", "ContentBg");
        MapFlowResource("ForegroundColor", "TextPrimary");
        MapFlowResource("AccentColor", "AccentColor");
    }

    private void MapFlowResource(string flowKey, string lertaroKey)
    {
        var val = TryFindResource(lertaroKey);
        if (val != null) Resources[flowKey] = val;
    }

    private void RegisterImplicitControlStyles()
    {
        if (TryFindResource(typeof(ContextMenu)) is Style cmStyle) Resources[typeof(ContextMenu)] = cmStyle;
        if (TryFindResource(typeof(MenuItem)) is Style miStyle) Resources[typeof(MenuItem)] = miStyle;

        // 1. Implicit TextBox Style with ContextMenu & themed validation error border
        var textBoxStyle = new Style(typeof(TextBox));
        textBoxStyle.Setters.Add(new Setter(BackgroundProperty, new DynamicResourceExtension("ControlBackground")));
        textBoxStyle.Setters.Add(new Setter(ForegroundProperty, new DynamicResourceExtension("TextPrimary")));
        textBoxStyle.Setters.Add(new Setter(BorderBrushProperty, new DynamicResourceExtension("ControlBorderBrush")));
        textBoxStyle.Setters.Add(new Setter(System.Windows.Controls.Primitives.TextBoxBase.CaretBrushProperty, new DynamicResourceExtension("TextPrimary")));
        textBoxStyle.Setters.Add(new Setter(Validation.ErrorTemplateProperty, null));
        textBoxStyle.Setters.Add(new Setter(BorderThicknessProperty, new Thickness(1)));
        textBoxStyle.Setters.Add(new Setter(PaddingProperty, new Thickness(6, 2, 6, 2)));
        textBoxStyle.Setters.Add(new Setter(VerticalContentAlignmentProperty, VerticalAlignment.Center));
        textBoxStyle.Setters.Add(new Setter(FontSizeProperty, 12.0));
        textBoxStyle.Setters.Add(new Setter(MinHeightProperty, 26.0));

        var tbContextMenu = new ContextMenu();
        tbContextMenu.Items.Add(new MenuItem { Command = ApplicationCommands.Cut });
        tbContextMenu.Items.Add(new MenuItem { Command = ApplicationCommands.Copy });
        tbContextMenu.Items.Add(new MenuItem { Command = ApplicationCommands.Paste });
        textBoxStyle.Setters.Add(new Setter(ContextMenuProperty, tbContextMenu));

        const string tbXaml = "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='TextBox'><Border x:Name='Border' CornerRadius='4' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}'><ScrollViewer x:Name='PART_ContentHost' Margin='2,0' VerticalAlignment='Center'/></Border><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Border' Property='BorderBrush' Value='{DynamicResource AccentBlue}'/></Trigger><Trigger Property='IsFocused' Value='True'><Setter TargetName='Border' Property='BorderBrush' Value='{DynamicResource AccentBlue}'/></Trigger><Trigger Property='Validation.HasError' Value='True'><Setter TargetName='Border' Property='BorderBrush' Value='{DynamicResource ErrorBrush}'/><Setter TargetName='Border' Property='BorderThickness' Value='1.5'/></Trigger></ControlTemplate.Triggers></ControlTemplate>";
        textBoxStyle.Setters.Add(new Setter(TemplateProperty, (ControlTemplate)XamlReader.Parse(tbXaml)));
        Resources[typeof(TextBox)] = textBoxStyle;

        // 2. Implicit Button Style
        var buttonStyle = new Style(typeof(Button));
        buttonStyle.Setters.Add(new Setter(BackgroundProperty, new DynamicResourceExtension("ControlBackground")));
        buttonStyle.Setters.Add(new Setter(ForegroundProperty, new DynamicResourceExtension("TextPrimary")));
        buttonStyle.Setters.Add(new Setter(BorderBrushProperty, new DynamicResourceExtension("ControlBorderBrush")));
        buttonStyle.Setters.Add(new Setter(BorderThicknessProperty, new Thickness(1)));
        buttonStyle.Setters.Add(new Setter(PaddingProperty, new Thickness(12, 4, 12, 4)));
        buttonStyle.Setters.Add(new Setter(FontSizeProperty, 12.0));
        buttonStyle.Setters.Add(new Setter(CursorProperty, Cursors.Hand));
        buttonStyle.Setters.Add(new Setter(MinHeightProperty, 26.0));

        const string btnXaml = "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='Button'><Grid><Border x:Name='Bd' CornerRadius='4' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}'/><Border x:Name='HoverOverlay' CornerRadius='4' Background='{DynamicResource ControlHoverBackground}' BorderBrush='{DynamicResource AccentBlue}' BorderThickness='{TemplateBinding BorderThickness}' Opacity='0'/><ContentPresenter Margin='{TemplateBinding Padding}' HorizontalAlignment='Center' VerticalAlignment='Center'/></Grid><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='HoverOverlay' Property='Opacity' Value='1'/></Trigger><Trigger Property='IsEnabled' Value='False'><Setter TargetName='Bd' Property='Background' Value='{DynamicResource ControlDisabledBackground}'/><Setter TargetName='Bd' Property='BorderBrush' Value='{DynamicResource ControlDisabledBorderBrush}'/><Setter Property='Foreground' Value='{DynamicResource ControlDisabledForeground}'/></Trigger></ControlTemplate.Triggers></ControlTemplate>";
        buttonStyle.Setters.Add(new Setter(TemplateProperty, (ControlTemplate)XamlReader.Parse(btnXaml)));
        Resources[typeof(Button)] = buttonStyle;

        // 3. Implicit ListBox Style
        var listBoxStyle = new Style(typeof(ListBox));
        listBoxStyle.Setters.Add(new Setter(BackgroundProperty, new DynamicResourceExtension("ControlBackground")));
        listBoxStyle.Setters.Add(new Setter(ForegroundProperty, new DynamicResourceExtension("TextPrimary")));
        listBoxStyle.Setters.Add(new Setter(BorderBrushProperty, new DynamicResourceExtension("ControlBorderBrush")));
        listBoxStyle.Setters.Add(new Setter(BorderThicknessProperty, new Thickness(1)));
        listBoxStyle.Setters.Add(new Setter(PaddingProperty, new Thickness(4)));

        const string lbXaml = "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='ListBox'><Border CornerRadius='6' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}'><ScrollViewer Focusable='False' Padding='{TemplateBinding Padding}'><ItemsPresenter/></ScrollViewer></Border></ControlTemplate>";
        listBoxStyle.Setters.Add(new Setter(TemplateProperty, (ControlTemplate)XamlReader.Parse(lbXaml)));
        Resources[typeof(ListBox)] = listBoxStyle;

        // 4. Implicit ListBoxItem Style with ItemSelected background, AccentBar, and hover feedback
        var lbiStyle = new Style(typeof(ListBoxItem));
        lbiStyle.Setters.Add(new Setter(ForegroundProperty, new DynamicResourceExtension("TextPrimary")));
        lbiStyle.Setters.Add(new Setter(PaddingProperty, new Thickness(8, 6, 8, 6)));
        lbiStyle.Setters.Add(new Setter(MarginProperty, new Thickness(0, 1, 0, 1)));
        lbiStyle.Setters.Add(new Setter(FocusVisualStyleProperty, null));
        lbiStyle.Setters.Add(new Setter(HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        lbiStyle.Setters.Add(new Setter(CursorProperty, Cursors.Hand));

        const string lbiXaml = "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='ListBoxItem'><Grid Margin='0,1'><Rectangle x:Name='AccentBar' Width='3' HorizontalAlignment='Left' RadiusX='1.5' RadiusY='1.5' Fill='Transparent' Margin='0,3'/><Border x:Name='ItemBorder' CornerRadius='6' Background='Transparent' Padding='{TemplateBinding Padding}' Margin='6,1,6,1'><ContentPresenter HorizontalAlignment='Stretch' VerticalAlignment='Center'/></Border></Grid><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='ItemBorder' Property='Background' Value='{DynamicResource ControlHoverBackground}'/></Trigger><Trigger Property='IsSelected' Value='True'><Setter TargetName='ItemBorder' Property='Background' Value='{DynamicResource ItemSelected}'/><Setter TargetName='AccentBar' Property='Fill' Value='{DynamicResource AccentBarColor}'/><Setter Property='Foreground' Value='{DynamicResource SidebarTextActive}'/><Setter Property='TextElement.Foreground' Value='{DynamicResource SidebarTextActive}'/></Trigger></ControlTemplate.Triggers></ControlTemplate>";
        lbiStyle.Setters.Add(new Setter(TemplateProperty, (ControlTemplate)XamlReader.Parse(lbiXaml)));
        Resources[typeof(ListBoxItem)] = lbiStyle;

        // 5. Implicit TextBlock Style
        var tbkStyle = new Style(typeof(TextBlock));
        tbkStyle.Setters.Add(new Setter(TextBlock.ForegroundProperty, new DynamicResourceExtension("TextPrimary")));
        tbkStyle.Setters.Add(new Setter(TextBlock.FontSizeProperty, 12.0));
        Resources[typeof(TextBlock)] = tbkStyle;
    }

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
        okButton.Click += (_, _) => Close();
        footerGrid.Children.Add(okButton);
        footerBar.Child = footerGrid;
        Grid.SetRow(footerBar, 2);
        rootGrid.Children.Add(footerBar);

        clipBorder.Child = rootGrid;
        outerBorder.Child = clipBorder;
        return outerBorder;
    }
}
