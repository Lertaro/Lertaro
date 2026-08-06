using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Lertaro.App.Converters;

/// <summary>Converts bool to Visibility (True → Visible, False → Collapsed).</summary>
public class BoolToVisibilityConverter : IValueConverter
{
    /// <summary>When true, inverts the conversion (False → Visible).</summary>
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var visible = value is bool b && b;
        if (Invert || parameter as string == "Invert")
            visible = !visible;
        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Visibility v && v == Visibility.Visible;
}

/// <summary>Converts a string to Visibility (non-empty -> Visible, empty/null -> Collapsed).</summary>
public class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var hasText = value is string s && !string.IsNullOrWhiteSpace(s);
        return hasText ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>Converts any reference to Visibility (non-null -> Visible, null -> Collapsed).</summary>
public class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value != null ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>Reference-equality check for two bound values, used to highlight the active tab when
/// tab identity is a live object (e.g. the selected plugin config Group) rather than a fixed string key.</summary>
public class ReferenceEqualsConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        => values.Length == 2 && ReferenceEquals(values[0], values[1]);

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
