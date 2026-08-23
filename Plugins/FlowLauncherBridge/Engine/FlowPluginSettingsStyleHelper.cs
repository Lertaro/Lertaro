using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;

namespace Lertaro.Plugins.FlowLauncherBridge.Engine;

/// <summary>
/// Helper for registering implicit dark theme styles on Flow plugin settings host windows.
/// Split out from FlowPluginSettingsHostWindow to keep file size under repo line limit.
/// </summary>
internal static class FlowPluginSettingsStyleHelper
{
    public static void MergeAppThemeDictionaries(ResourceDictionary resources)
    {
        try
        {
            var uris = new[] { "Styles.xaml", "Styles/Controls/Menu.xaml", "Styles/Windows/SearchWindow.xaml", "Styles/Windows/SettingsWindow.xaml", "Styles/Windows/SettingsComboBox.xaml" };
            foreach (var u in uris)
            {
                resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"pack://application:,,,/Lertaro.App;component/Resources/{u}", UriKind.Absolute) });
            }
        }
        catch { }

        MapFlowResource(resources, "ItemTitleColor", "TextPrimary");
        MapFlowResource(resources, "ItemSubTitleColor", "TextSecondary");
        MapFlowResource(resources, "WindowBackground", "ContentBg");
        MapFlowResource(resources, "BackgroundColor", "ContentBg");
        MapFlowResource(resources, "ForegroundColor", "TextPrimary");
        MapFlowResource(resources, "AccentColor", "AccentColor");
    }

    private static void MapFlowResource(ResourceDictionary resources, string flowKey, string lertaroKey)
    {
        if (resources.Contains(lertaroKey))
        {
            resources[flowKey] = resources[lertaroKey];
        }
    }

    public static void RegisterImplicitControlStyles(ResourceDictionary resources)
    {
        if (resources[typeof(ContextMenu)] is Style cmStyle) resources[typeof(ContextMenu)] = cmStyle;
        if (resources[typeof(MenuItem)] is Style miStyle) resources[typeof(MenuItem)] = miStyle;

        // 1. Implicit TextBox Style
        var textBoxStyle = new Style(typeof(TextBox));
        textBoxStyle.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension("ControlBackground")));
        textBoxStyle.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("TextPrimary")));
        textBoxStyle.Setters.Add(new Setter(Control.BorderBrushProperty, new DynamicResourceExtension("ControlBorderBrush")));
        textBoxStyle.Setters.Add(new Setter(System.Windows.Controls.Primitives.TextBoxBase.CaretBrushProperty, new DynamicResourceExtension("TextPrimary")));
        textBoxStyle.Setters.Add(new Setter(Validation.ErrorTemplateProperty, null));
        textBoxStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        textBoxStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(6, 2, 6, 2)));
        textBoxStyle.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
        textBoxStyle.Setters.Add(new Setter(Control.FontSizeProperty, 12.0));
        textBoxStyle.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 26.0));

        var tbContextMenu = new ContextMenu();
        tbContextMenu.Items.Add(new MenuItem { Command = ApplicationCommands.Cut });
        tbContextMenu.Items.Add(new MenuItem { Command = ApplicationCommands.Copy });
        tbContextMenu.Items.Add(new MenuItem { Command = ApplicationCommands.Paste });
        textBoxStyle.Setters.Add(new Setter(FrameworkElement.ContextMenuProperty, tbContextMenu));

        const string tbXaml = "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='TextBox'><Border x:Name='Border' CornerRadius='4' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}'><ScrollViewer x:Name='PART_ContentHost' Margin='2,0' VerticalAlignment='Center'/></Border><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Border' Property='BorderBrush' Value='{DynamicResource AccentBlue}'/></Trigger><Trigger Property='IsFocused' Value='True'><Setter TargetName='Border' Property='BorderBrush' Value='{DynamicResource AccentBlue}'/></Trigger><Trigger Property='Validation.HasError' Value='True'><Setter TargetName='Border' Property='BorderBrush' Value='{DynamicResource ErrorBrush}'/><Setter TargetName='Border' Property='BorderThickness' Value='1.5'/></Trigger></ControlTemplate.Triggers></ControlTemplate>";
        textBoxStyle.Setters.Add(new Setter(Control.TemplateProperty, (ControlTemplate)XamlReader.Parse(tbXaml)));
        resources[typeof(TextBox)] = textBoxStyle;

        // 2. Implicit Button Style
        var buttonStyle = new Style(typeof(Button));
        buttonStyle.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension("ControlBackground")));
        buttonStyle.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("TextPrimary")));
        buttonStyle.Setters.Add(new Setter(Control.BorderBrushProperty, new DynamicResourceExtension("ControlBorderBrush")));
        buttonStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        buttonStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(12, 4, 12, 4)));
        buttonStyle.Setters.Add(new Setter(Control.FontSizeProperty, 12.0));
        buttonStyle.Setters.Add(new Setter(FrameworkElement.CursorProperty, Cursors.Hand));
        buttonStyle.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 26.0));

        const string btnXaml = "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='Button'><Grid><Border x:Name='Bd' CornerRadius='4' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}'/><Border x:Name='HoverOverlay' CornerRadius='4' Background='{DynamicResource ControlHoverBackground}' BorderBrush='{DynamicResource AccentBlue}' BorderThickness='{TemplateBinding BorderThickness}' Opacity='0'/><ContentPresenter Margin='{TemplateBinding Padding}' HorizontalAlignment='Center' VerticalAlignment='Center'/></Grid><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='HoverOverlay' Property='Opacity' Value='1'/></Trigger><Trigger Property='IsEnabled' Value='False'><Setter TargetName='Bd' Property='Background' Value='{DynamicResource ControlDisabledBackground}'/><Setter TargetName='Bd' Property='BorderBrush' Value='{DynamicResource ControlDisabledBorderBrush}'/><Setter Property='Foreground' Value='{DynamicResource ControlDisabledForeground}'/></Trigger></ControlTemplate.Triggers></ControlTemplate>";
        buttonStyle.Setters.Add(new Setter(Control.TemplateProperty, (ControlTemplate)XamlReader.Parse(btnXaml)));
        resources[typeof(Button)] = buttonStyle;

        // 3. Implicit ListBox & ListBoxItem
        var listBoxStyle = new Style(typeof(ListBox));
        listBoxStyle.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension("SidebarBg")));
        listBoxStyle.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("TextPrimary")));
        listBoxStyle.Setters.Add(new Setter(Control.BorderBrushProperty, new DynamicResourceExtension("BorderColor")));
        listBoxStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        listBoxStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(4)));

        const string lbXaml = "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='ListBox'><Border CornerRadius='6' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}'><ScrollViewer Focusable='False' Padding='{TemplateBinding Padding}'><ItemsPresenter/></ScrollViewer></Border></ControlTemplate>";
        listBoxStyle.Setters.Add(new Setter(Control.TemplateProperty, (ControlTemplate)XamlReader.Parse(lbXaml)));
        resources[typeof(ListBox)] = listBoxStyle;

        var lbiStyle = new Style(typeof(ListBoxItem));
        lbiStyle.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("TextPrimary")));
        lbiStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 6, 8, 6)));
        lbiStyle.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0, 1, 0, 1)));
        lbiStyle.Setters.Add(new Setter(FrameworkElement.FocusVisualStyleProperty, null));
        lbiStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        lbiStyle.Setters.Add(new Setter(FrameworkElement.CursorProperty, Cursors.Hand));

        const string lbiXaml = "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='ListBoxItem'><Grid Margin='0,1'><Rectangle x:Name='AccentBar' Width='3' HorizontalAlignment='Left' RadiusX='1.5' RadiusY='1.5' Fill='Transparent' Margin='0,3'/><Border x:Name='ItemBorder' CornerRadius='6' Background='Transparent' Padding='{TemplateBinding Padding}' Margin='6,1,6,1'><ContentPresenter HorizontalAlignment='Stretch' VerticalAlignment='Center'/></Border></Grid><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='ItemBorder' Property='Background' Value='{DynamicResource SidebarHover}'/></Trigger><Trigger Property='IsSelected' Value='True'><Setter TargetName='ItemBorder' Property='Background' Value='{DynamicResource SidebarHover}'/><Setter TargetName='AccentBar' Property='Fill' Value='{DynamicResource AccentBarColor}'/><Setter Property='Foreground' Value='{DynamicResource SidebarTextActive}'/><Setter Property='TextElement.Foreground' Value='{DynamicResource SidebarTextActive}'/></Trigger></ControlTemplate.Triggers></ControlTemplate>";
        lbiStyle.Setters.Add(new Setter(Control.TemplateProperty, (ControlTemplate)XamlReader.Parse(lbiXaml)));
        resources[typeof(ListBoxItem)] = lbiStyle;

        // 4. Implicit TextBlock Style
        var tbkStyle = new Style(typeof(TextBlock));
        tbkStyle.Setters.Add(new Setter(TextBlock.ForegroundProperty, new DynamicResourceExtension("TextPrimary")));
        tbkStyle.Setters.Add(new Setter(TextBlock.FontSizeProperty, 12.0));
        resources[typeof(TextBlock)] = tbkStyle;

        // 5. Implicit ComboBox & ComboBoxItem Styles
        if (resources["SettingsComboBox"] is Style cbStyle) resources[typeof(ComboBox)] = new Style(typeof(ComboBox), cbStyle);
        if (resources["SettingsComboBoxItem"] is Style cbiStyle) resources[typeof(ComboBoxItem)] = new Style(typeof(ComboBoxItem), cbiStyle);
    }
}
