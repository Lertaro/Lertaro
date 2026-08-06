using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Lertaro.App.Views.QuickPanel;

/// <summary>Stacks detail rows after one gutter shared with every visible quick-panel group.</summary>
internal sealed class QuickPanelNumberedStackPanel : System.Windows.Controls.Panel
{
    public static readonly DependencyProperty MaximumItemCountProperty = DependencyProperty.Register(
        nameof(MaximumItemCount), typeof(int), typeof(QuickPanelNumberedStackPanel),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    private double _gutterWidth;

    public int MaximumItemCount { get => (int)GetValue(MaximumItemCountProperty); set => SetValue(MaximumItemCountProperty, value); }

    protected override System.Windows.Size MeasureOverride(System.Windows.Size availableSize)
    {
        _gutterWidth = GutterWidth(MaximumItemCount);
        var width = double.IsInfinity(availableSize.Width) ? 0 : Math.Max(0, availableSize.Width - _gutterWidth);
        var height = 0d;
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new System.Windows.Size(width, double.PositiveInfinity));
            height += child.DesiredSize.Height;
            width = Math.Max(width, child.DesiredSize.Width);
        }

        return new System.Windows.Size(width + _gutterWidth, height);
    }

    protected override System.Windows.Size ArrangeOverride(System.Windows.Size finalSize)
    {
        _gutterWidth = GutterWidth(MaximumItemCount);
        var top = 0d;
        var width = Math.Max(0, finalSize.Width - _gutterWidth);
        foreach (UIElement child in InternalChildren)
        {
            var height = child.DesiredSize.Height;
            child.Arrange(new Rect(_gutterWidth, top, width, height));
            top += height;
        }

        return finalSize;
    }

    protected override void OnRender(DrawingContext context)
    {
        var digits = QuickPanelLineNumberLayout.DigitsFor(MaximumItemCount);
        var top = 0d;
        for (var index = 0; index < InternalChildren.Count; index++)
        {
            var text = NumberText(index + 1, digits);
            var height = InternalChildren[index].RenderSize.Height;
            context.DrawText(text, new System.Windows.Point(_gutterWidth - text.Width - 8, top + (height - text.Height) / 2));
            top += height;
        }
    }

    private double GutterWidth(int maximumItemCount) => NumberText(maximumItemCount, QuickPanelLineNumberLayout.DigitsFor(maximumItemCount)).Width + 8;

    private FormattedText NumberText(int number, int digits) => new(
        number.ToString($"D{digits}", CultureInfo.CurrentCulture), CultureInfo.CurrentCulture,
        System.Windows.FlowDirection.LeftToRight, new Typeface("Segoe UI"), 10, System.Windows.Media.Brushes.Gray,
        VisualTreeHelper.GetDpi(this).PixelsPerDip);
}
