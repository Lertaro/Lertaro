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

    /// <summary>The width of the picture in a tile, which the template binds each one to.</summary>
    /// <remarks>
    /// Published from here rather than worked out in the template, because this panel is the only thing
    /// that knows the slot it handed out: a tile has no width of its own to divide (see the template), and
    /// the arithmetic that turns the panel's width into a slot, and a slot into a picture, lives here. The
    /// template binding to it is what keeps the two halves of that sum from drifting apart.
    ///
    /// Republished on every measure, which is what makes a Ctrl+wheel step visible: the scale it reads is
    /// ambient, so the panel is re-measured when it changes (see QuickPanelWindowInput.ApplyIconScale) and
    /// the binding here picks up the new value. A binding to the scale itself could not have been used --
    /// the picture's size depends on the slot this panel calculated, which a stored setting cannot express.
    /// </remarks>
    private static readonly DependencyPropertyKey IconWidthKey = DependencyProperty.RegisterReadOnly(
        nameof(IconWidth), typeof(double), typeof(QuickPanelNumberedWrapPanel),
        new FrameworkPropertyMetadata(0.0));

    public static readonly DependencyProperty IconWidthProperty = IconWidthKey.DependencyProperty;

    public double IconWidth => (double)GetValue(IconWidthProperty);

    /// <summary>The height of the picture in a tile: the same box for every tile, wider than tall.</summary>
    private static readonly DependencyPropertyKey IconHeightKey = DependencyProperty.RegisterReadOnly(
        nameof(IconHeight), typeof(double), typeof(QuickPanelNumberedWrapPanel),
        new FrameworkPropertyMetadata(0.0));

    public static readonly DependencyProperty IconHeightProperty = IconHeightKey.DependencyProperty;

    public double IconHeight => (double)GetValue(IconHeightProperty);

    private double _gutterWidth;
    private double _slotWidth;
    private int _columns;

    public int MaximumItemCount { get => (int)GetValue(MaximumItemCountProperty); set => SetValue(MaximumItemCountProperty, value); }

    internal int Columns => _columns;

    /// <summary>Recomputes the picture size from the slot just worked out, and publishes it.</summary>
    /// <remarks>
    /// Set rather than assigned to a field, and set even when the value is unchanged: SetValue with an
    /// equal value is a no-op in WPF, so this costs nothing on a measure that changed nothing, and the
    /// first measure after a scale change is what the template's binding picks up.
    /// </remarks>
    private void PublishIconSize()
    {
        SetValue(IconWidthKey, QuickPanelTileMetrics.IconWidthFor(_slotWidth));
        SetValue(IconHeightKey, QuickPanelTileMetrics.IconHeightFor(_slotWidth));
    }

    protected override System.Windows.Size MeasureOverride(System.Windows.Size availableSize)
    {
        CalculateLayout(availableSize.Width);
        PublishIconSize();
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

    /// <summary>Works out the gutter, the column count and the slot for a row this wide.</summary>
    /// <remarks>
    /// The scale goes into BOTH the column count and the slot, and they are the same decision seen twice:
    /// the count is measured against the tile size the scale asks for, and the slot is the width divided
    /// by however many that came to. Dividing the slot by the scale as well would shrink the tiles twice
    /// over -- once in the count and again in the division -- leaving a row of tiny tiles and a long empty
    /// strip after them, which is the thing the divide-the-width rule exists to avoid.
    ///
    /// Two passes because each depends on the other: the gutter is sized for the row count, the row count
    /// depends on the columns, and the columns are measured after the gutter is taken out of the width.
    /// </remarks>
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
