using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Lertaro.App.Helpers.Visuals;

/// <summary>Shows a tooltip only when a TextBlock, or text inside a container, is actually trimmed.</summary>
internal static class TrimmedTextToolTip
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.RegisterAttached(
        "Text", typeof(string), typeof(TrimmedTextToolTip), new PropertyMetadata(null, OnTextChanged));

    public static string? GetText(DependencyObject element) => (string?)element.GetValue(TextProperty);
    public static void SetText(DependencyObject element, string? value) => element.SetValue(TextProperty, value);

    private static void OnTextChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not FrameworkElement element)
            return;

        element.ToolTipOpening -= OnToolTipOpening;
        if (e.NewValue != null)
            element.ToolTipOpening += OnToolTipOpening;
        element.ToolTip = e.NewValue;
    }

    private static void OnToolTipOpening(object sender, ToolTipEventArgs e)
    {
        if (sender is FrameworkElement element && !ShouldShowToolTip(element))
            e.Handled = true;
    }

    internal static bool ShouldShowToolTip(FrameworkElement element)
    {
        if (element is TextBlock textBlock)
            return IsTrimmed(textBlock);

        var textBlocks = Descendants(element).Where(text => text.TextTrimming != TextTrimming.None).ToList();
        return textBlocks.Count == 0 || textBlocks.Any(IsTrimmed);
    }

    internal static bool IsTrimmed(TextBlock textBlock)
    {
        if (textBlock.TextTrimming == TextTrimming.None)
            return false;
        if (textBlock.Visibility != Visibility.Visible)
            return !string.IsNullOrEmpty(textBlock.Text);
        if (textBlock.ActualWidth <= 0)
            return false;

        var typeface = new Typeface(textBlock.FontFamily, textBlock.FontStyle, textBlock.FontWeight, textBlock.FontStretch);
        var text = new FormattedText(textBlock.Text ?? string.Empty, CultureInfo.CurrentUICulture,
            textBlock.FlowDirection, typeface, textBlock.FontSize, System.Windows.Media.Brushes.Transparent,
            VisualTreeHelper.GetDpi(textBlock).PixelsPerDip);
        var availableWidth = Math.Max(0, textBlock.ActualWidth - textBlock.Padding.Left - textBlock.Padding.Right);
        return text.WidthIncludingTrailingWhitespace > availableWidth + 0.5;
    }

    private static IEnumerable<TextBlock> Descendants(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is TextBlock textBlock)
                yield return textBlock;
            foreach (var descendant in Descendants(child))
                yield return descendant;
        }
    }
}
