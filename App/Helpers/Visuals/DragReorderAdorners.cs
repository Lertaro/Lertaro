using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace Lertaro.App.Helpers.Visuals;

// The two adorners a DragReorder drag paints: the floating snapshot of the row being carried, and the
// line showing where it would land. Split out of DragReorder.cs purely to keep that file under the
// repo's per-file line limit; neither has any state beyond what the drag hands it, and nothing outside
// that one drag ever constructs either.

// A VisualBrush snapshot of the dragged row, hosted in a real Border child (not just painted in
// OnRender) specifically so it can carry a genuine DropShadowEffect -- Adorner.OnRender's
// DrawingContext has no Effect concept of its own.
internal sealed class DragAdorner : Adorner
{
    private readonly Border _visual;
    private Point _position;

    public DragAdorner(FrameworkElement source, UIElement adornedElement, Point startPosition) : base(adornedElement)
    {
        IsHitTestVisible = false;
        _position = startPosition;

        _visual = new Border
        {
            Width = source.ActualWidth,
            Height = source.ActualHeight,
            Background = new VisualBrush(source) { Stretch = Stretch.None },
            Opacity = 0.85,
            Effect = new DropShadowEffect { BlurRadius = 16, ShadowDepth = 3, Opacity = 0.45, Color = Colors.Black },
        };
        AddVisualChild(_visual);
    }

    protected override int VisualChildrenCount => 1;
    protected override Visual GetVisualChild(int index) => _visual;

    protected override Size MeasureOverride(Size constraint)
    {
        _visual.Measure(constraint);
        return _visual.DesiredSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _visual.Arrange(new Rect(_position.X - _visual.Width / 2, _position.Y - _visual.Height / 2, _visual.Width, _visual.Height));
        return finalSize;
    }

    public void UpdatePosition(Point position)
    {
        _position = position;
        InvalidateArrange();
    }
}

// The line marking exactly where the dragged row would land -- drawn clear across the ItemsControl at
// whichever row edge OnDragOver's UpdateDropIndicator computes, hidden (not removed) between updates
// so it doesn't need to be re-added to the AdornerLayer every frame. Across the width at a row's edge
// for a vertical list, down the height at a tab's edge for a horizontal one.
internal sealed class DropIndicatorAdorner : Adorner
{
    private double _offset;
    private double _length;
    private bool _visible;
    private bool _horizontal;
    private bool _grid;
    private Rect _gridBounds;
    private readonly Pen _pen;

    public DropIndicatorAdorner(UIElement adornedElement) : base(adornedElement)
    {
        IsHitTestVisible = false;

        var brush = System.Windows.Application.Current?.TryFindResource("AccentBlue") as SolidColorBrush
                    ?? System.Windows.Media.Brushes.DodgerBlue;
        _pen = new Pen(brush, 2);
        _pen.Freeze();
    }

    public void Update(double offset, double length, bool visible, bool horizontal = false)
    {
        _offset = offset;
        _length = length;
        _visible = visible;
        _horizontal = horizontal;
        _grid = false;
        InvalidateVisual();
    }

    public void UpdateGrid(Rect bounds, bool after)
    {
        _gridBounds = bounds;
        _offset = after ? bounds.Right : bounds.Left;
        _visible = true;
        _grid = true;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        if (!_visible) return;

        if (_grid)
        {
            drawingContext.DrawLine(_pen, new Point(_offset, _gridBounds.Top), new Point(_offset, _gridBounds.Bottom));
            return;
        }

        drawingContext.DrawLine(_pen,
            _horizontal ? new Point(_offset, 0) : new Point(0, _offset),
            _horizontal ? new Point(_offset, _length) : new Point(_length, _offset));
    }
}
