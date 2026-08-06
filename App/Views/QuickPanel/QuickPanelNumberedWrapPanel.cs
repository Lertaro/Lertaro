using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Lertaro.App.Converters;

namespace Lertaro.App.Views.QuickPanel;

/// <summary>Arranges thumbnail tiles after one gutter shared with every visible quick-panel group.</summary>
internal sealed class QuickPanelNumberedWrapPanel : System.Windows.Controls.Panel
{
    public static readonly DependencyProperty MaximumItemCountProperty = DependencyProperty.Register(
        nameof(MaximumItemCount), typeof(int), typeof(QuickPanelNumberedWrapPanel),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    private double _gutterWidth;
    private double _slotWidth;
    private int _columns;

    public int MaximumItemCount { get => (int)GetValue(MaximumItemCountProperty); set => SetValue(MaximumItemCountProperty, value); }

    protected override System.Windows.Size MeasureOverride(System.Windows.Size availableSize)
    {
        CalculateLayout(availableSize.Width);
        var height = QuickPanelLineNumberLayout.RowsFor(InternalChildren.Count, _columns) * CellHeight();
        foreach (UIElement child in InternalChildren)
            child.Measure(new System.Windows.Size(_slotWidth, CellHeight()));
        return new System.Windows.Size(availableSize.Width, height);
    }

    protected override System.Windows.Size ArrangeOverride(System.Windows.Size finalSize)
    {
        CalculateLayout(finalSize.Width);
        var height = CellHeight();
        for (var index = 0; index < InternalChildren.Count; index++)
            InternalChildren[index].Arrange(new Rect(
                _gutterWidth + index % _columns * _slotWidth, index / _columns * height, _slotWidth, height));
        return finalSize;
    }

    protected override void OnRender(DrawingContext context)
    {
        var digits = QuickPanelLineNumberLayout.DigitsFor(QuickPanelLineNumberLayout.RowsFor(MaximumItemCount, _columns));
        var rows = QuickPanelLineNumberLayout.RowsFor(InternalChildren.Count, _columns);
        var height = CellHeight();
        var iconHeight = QuickPanelTileMetrics.IconHeightFor(_slotWidth);
        for (var row = 0; row < rows; row++)
        {
            var text = NumberText(row + 1, digits);
            context.DrawText(text, new System.Windows.Point(_gutterWidth - text.Width - 8, row * height + (iconHeight - text.Height) / 2));
        }
    }

    private void CalculateLayout(double width)
    {
        _gutterWidth = GutterWidth(QuickPanelLineNumberLayout.RowsFor(MaximumItemCount, 1));
        for (var pass = 0; pass < 2; pass++)
        {
            _columns = QuickPanelLineNumberLayout.ThumbnailColumnsFor(width, _gutterWidth);
            _gutterWidth = GutterWidth(QuickPanelLineNumberLayout.RowsFor(MaximumItemCount, _columns));
        }
        _slotWidth = QuickPanelTileMetrics.SlotFor(Math.Max(0, width - _gutterWidth));
    }

    private double CellHeight() => QuickPanelTileMetrics.CellHeightFor(_slotWidth);
    private double GutterWidth(int maximumRowCount) => NumberText(maximumRowCount, QuickPanelLineNumberLayout.DigitsFor(maximumRowCount)).Width + 8;
    private FormattedText NumberText(int number, int digits) => new(
        number.ToString($"D{digits}", CultureInfo.CurrentCulture), CultureInfo.CurrentCulture,
        System.Windows.FlowDirection.LeftToRight, new Typeface("Segoe UI"), 10, System.Windows.Media.Brushes.Gray,
        VisualTreeHelper.GetDpi(this).PixelsPerDip);
}
