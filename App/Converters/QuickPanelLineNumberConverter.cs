using System.Globalization;
using System.Windows.Data;

namespace Lertaro.App.Converters;

/// <summary>Formats an item's zero-based alternation index as the visible list line number.</summary>
public sealed class QuickPanelLineNumberConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        => values.Length == 2 && values[0] is int index && values[1] is int count
            ? Format(index + 1, count, culture)
            : string.Empty;

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();

    internal static string Format(int line, int count, CultureInfo culture)
        => line.ToString($"D{Math.Max(1, count.ToString(culture).Length)}", culture);
}
