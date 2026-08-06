using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Pen = System.Windows.Media.Pen;

namespace Lertaro.App.Helpers.Visuals;

// The box itself: a translucent accent fill with a solid edge, the shape every file manager draws for
// this. Split out of RubberBandSelection.cs to keep that file focused on the gesture; it has no state
// beyond the rectangle it is handed.
internal sealed class RubberBandAdorner : Adorner
{
    private Rect _box;
    private readonly Brush _fill;
    private readonly Pen _edge;

    public RubberBandAdorner(UIElement adornedElement) : base(adornedElement)
    {
        // Never in the way of the drag that is drawing it -- an adorner that took hits would swallow
        // every move the moment the box grew under the pointer.
        IsHitTestVisible = false;

        var accent = System.Windows.Application.Current?.TryFindResource("AccentBlue") as SolidColorBrush
                     ?? System.Windows.Media.Brushes.DodgerBlue;

        _fill = new SolidColorBrush(accent.Color) { Opacity = 0.18 };
        _fill.Freeze();
        _edge = new Pen(new SolidColorBrush(accent.Color), 1);
        _edge.Freeze();
    }

    public void Update(Rect box)
    {
        _box = box;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        if (_box.IsEmpty || _box.Width <= 0 || _box.Height <= 0) return;

        drawingContext.DrawRectangle(_fill, _edge, _box);
    }
}
