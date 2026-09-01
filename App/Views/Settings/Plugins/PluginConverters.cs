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
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not PluginComponentType type)
            return new SolidColorBrush(WpfColor.FromRgb(0x6B, 0x72, 0x80));

        return type switch
        {
            PluginComponentType.Action => new SolidColorBrush(WpfColor.FromRgb(0x3B, 0x82, 0xF6)),
            PluginComponentType.DynamicActionProvider => new SolidColorBrush(WpfColor.FromRgb(0x8B, 0x5C, 0xF6)),
            PluginComponentType.InstantProvider => new SolidColorBrush(WpfColor.FromRgb(0x10, 0xB9, 0x81)),
            PluginComponentType.FullSearchFileResultProvider => new SolidColorBrush(WpfColor.FromRgb(0x06, 0xB6, 0xD4)),
            PluginComponentType.SearchableItemProvider => new SolidColorBrush(WpfColor.FromRgb(0xD9, 0x46, 0xEF)),
            PluginComponentType.FilterProvider => new SolidColorBrush(WpfColor.FromRgb(0xF5, 0x9E, 0x0B)),
            PluginComponentType.ColumnProvider => new SolidColorBrush(WpfColor.FromRgb(0x63, 0x66, 0xF1)),
            PluginComponentType.AliasProvider => new SolidColorBrush(WpfColor.FromRgb(0xEC, 0x48, 0x99)),
            PluginComponentType.ActivePathCollector => new SolidColorBrush(WpfColor.FromRgb(0x0D, 0x94, 0x88)),
            PluginComponentType.FileDialogAdapter => new SolidColorBrush(WpfColor.FromRgb(0xF9, 0x73, 0x16)),
            PluginComponentType.InlineSearchAdapter => new SolidColorBrush(WpfColor.FromRgb(0xEF, 0x44, 0x44)),
            PluginComponentType.FilePreviewProvider => new SolidColorBrush(WpfColor.FromRgb(0x14, 0xB8, 0xA6)),
            PluginComponentType.QuickNavigationProvider => new SolidColorBrush(WpfColor.FromRgb(0x0E, 0x74, 0x90)),
            PluginComponentType.ThumbnailProvider => new SolidColorBrush(WpfColor.FromRgb(0x06, 0xB6, 0xD4)),
            PluginComponentType.QueryTokenProvider => new SolidColorBrush(WpfColor.FromRgb(0x84, 0xCC, 0x16)),
            PluginComponentType.QuickPanelTabProvider => new SolidColorBrush(WpfColor.FromRgb(0x8B, 0x5C, 0xF6)),
            _ => new SolidColorBrush(WpfColor.FromRgb(0x6B, 0x72, 0x80))
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
