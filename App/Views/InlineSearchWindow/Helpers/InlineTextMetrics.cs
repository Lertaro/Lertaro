using System.Globalization;
using System.Windows.Controls;
using System.Windows.Media;

namespace Lertaro.App.Views.InlineSearchWindow.Helpers;

/// <summary>
/// Measures inline result text with the same font settings as its rendered TextBlock.
/// </summary>
internal static class InlineTextMetrics
{
    internal static double Measure(string text, TextBlock sample)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        var dpi = VisualTreeHelper.GetDpi(sample);
        var typeface = new Typeface(sample.FontFamily, sample.FontStyle, sample.FontWeight, sample.FontStretch);
        var formatted = new FormattedText(text, CultureInfo.CurrentUICulture, System.Windows.FlowDirection.LeftToRight,
            typeface, sample.FontSize, System.Windows.Media.Brushes.Transparent, dpi.PixelsPerDip);
        return formatted.WidthIncludingTrailingWhitespace;
    }
}
