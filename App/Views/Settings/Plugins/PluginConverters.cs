using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Lertaro.App.Services;
using Lertaro.App.ViewModels.Settings.Plugins;

// Alias to avoid ambiguity with System.Drawing.Color
using WpfColor = System.Windows.Media.Color;

namespace Lertaro.App.Views.Settings.Plugins;

/// <summary>Converts a plugin component type enum to its localized label.</summary>
public class ComponentTypeToLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not PluginComponentType type)
            return string.Empty;

        return TranslationManager.Instance[$"Plugins_Type{type}"];
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>Converts a plugin component type enum to a SolidColorBrush for UI badging.</summary>
public class ComponentTypeToBadgeBrushConverter : IValueConverter
{
    // One frozen brush per colour, built once. These used to be allocated anew on every Convert (twice
    // per component group header), i.e. a fresh freezable per header on every card render.
    private static readonly SolidColorBrush FallbackBrush = Frozen(0x6B, 0x72, 0x80);
    private static readonly SolidColorBrush ActionBrush = Frozen(0x3B, 0x82, 0xF6);
    private static readonly SolidColorBrush DynamicActionProviderBrush = Frozen(0x8B, 0x5C, 0xF6);
    private static readonly SolidColorBrush InstantProviderBrush = Frozen(0x10, 0xB9, 0x81);
    private static readonly SolidColorBrush FullSearchFileResultProviderBrush = Frozen(0x06, 0xB6, 0xD4);
    private static readonly SolidColorBrush SearchableItemProviderBrush = Frozen(0xD9, 0x46, 0xEF);
    private static readonly SolidColorBrush FilterProviderBrush = Frozen(0xF5, 0x9E, 0x0B);
    private static readonly SolidColorBrush ColumnProviderBrush = Frozen(0x63, 0x66, 0xF1);
    private static readonly SolidColorBrush AliasProviderBrush = Frozen(0xEC, 0x48, 0x99);
    private static readonly SolidColorBrush ActivePathCollectorBrush = Frozen(0x0D, 0x94, 0x88);
    private static readonly SolidColorBrush FileDialogAdapterBrush = Frozen(0xF9, 0x73, 0x16);
    private static readonly SolidColorBrush InlineSearchAdapterBrush = Frozen(0xEF, 0x44, 0x44);
    private static readonly SolidColorBrush FilePreviewProviderBrush = Frozen(0x14, 0xB8, 0xA6);
    private static readonly SolidColorBrush QuickNavigationProviderBrush = Frozen(0x0E, 0x74, 0x90);
    private static readonly SolidColorBrush ThumbnailProviderBrush = Frozen(0x06, 0xB6, 0xD4);
    private static readonly SolidColorBrush QueryTokenProviderBrush = Frozen(0x84, 0xCC, 0x16);
    private static readonly SolidColorBrush QuickPanelTabProviderBrush = Frozen(0x8B, 0x5C, 0xF6);

    private static SolidColorBrush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(WpfColor.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not PluginComponentType type)
            return FallbackBrush;

        return type switch
        {
            PluginComponentType.Action => ActionBrush,
            PluginComponentType.DynamicActionProvider => DynamicActionProviderBrush,
            PluginComponentType.InstantProvider => InstantProviderBrush,
            PluginComponentType.FullSearchFileResultProvider => FullSearchFileResultProviderBrush,
            PluginComponentType.SearchableItemProvider => SearchableItemProviderBrush,
            PluginComponentType.FilterProvider => FilterProviderBrush,
            PluginComponentType.ColumnProvider => ColumnProviderBrush,
            PluginComponentType.AliasProvider => AliasProviderBrush,
            PluginComponentType.ActivePathCollector => ActivePathCollectorBrush,
            PluginComponentType.FileDialogAdapter => FileDialogAdapterBrush,
            PluginComponentType.InlineSearchAdapter => InlineSearchAdapterBrush,
            PluginComponentType.FilePreviewProvider => FilePreviewProviderBrush,
            PluginComponentType.QuickNavigationProvider => QuickNavigationProviderBrush,
            PluginComponentType.ThumbnailProvider => ThumbnailProviderBrush,
            PluginComponentType.QueryTokenProvider => QueryTokenProviderBrush,
            PluginComponentType.QuickPanelTabProvider => QuickPanelTabProviderBrush,
            _ => FallbackBrush
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>Converts an empty/whitespace string to a localized "untitled" placeholder.</summary>
public class EmptyStringToPlaceholderConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var text = value as string;
        return string.IsNullOrWhiteSpace(text) ? TranslationManager.Instance["Plugins_Config_UntitledItem"] : text!;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>Converts an array item's badge field VM to Visibility: Collapsed when the field is
/// absent (no second Text sub-field in the schema) or its value is an empty/whitespace string.
/// Binding straight to "BadgeField.Value" breaks the path (and silently falls back to the
/// default Visible) whenever BadgeField itself is null, so this converts on the field object.</summary>
public class BadgeFieldToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var text = (value as PluginConfigFieldViewModel)?.Value as string;
        return string.IsNullOrWhiteSpace(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
