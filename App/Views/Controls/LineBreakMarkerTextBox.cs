using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfPoint = System.Windows.Point;

namespace Lertaro.App.Views.Controls;

// Displays real line breaks without changing the edited text; soft-wrapped lines remain unmarked.
public sealed class LineBreakMarkerTextBox : WpfTextBox
{
    private LineBreakMarkerAdorner? _markerAdorner;

    public LineBreakMarkerTextBox()
    {
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    protected override void OnTextChanged(TextChangedEventArgs e)
    {
        base.OnTextChanged(e);
        InvalidateMarkersLater();
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        _markerAdorner?.InvalidateVisual();
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == ForegroundProperty || e.Property == FontFamilyProperty
            || e.Property == FontSizeProperty || e.Property == FontStyleProperty
            || e.Property == FontWeightProperty || e.Property == FontStretchProperty)
            _markerAdorner?.InvalidateVisual();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var layer = AdornerLayer.GetAdornerLayer(this);
        if (layer == null) return;

        _markerAdorner = new LineBreakMarkerAdorner(this);
        layer.Add(_markerAdorner);
        InvalidateMarkersLater();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_markerAdorner is { } adorner)
        {
            AdornerLayer.GetAdornerLayer(this)?.Remove(adorner);
            _markerAdorner = null;
        }
    }

    private void InvalidateMarkersLater() => Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
                                                      _markerAdorner?.InvalidateVisual()));

    private sealed class LineBreakMarkerAdorner : Adorner
    {
        private readonly LineBreakMarkerTextBox _owner;

        public LineBreakMarkerAdorner(LineBreakMarkerTextBox owner) : base(owner)
        {
            _owner = owner;
            IsHitTestVisible = false;
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            base.OnRender(drawingContext);
            if (string.IsNullOrEmpty(_owner.Text) || _owner.Foreground == null) return;

            var typeface = new Typeface(_owner.FontFamily, _owner.FontStyle,
                _owner.FontWeight, _owner.FontStretch);
            var pixelsPerDip = VisualTreeHelper.GetDpi(_owner).PixelsPerDip;
            var marker = new FormattedText("↵", CultureInfo.CurrentCulture, _owner.FlowDirection,
                typeface, _owner.FontSize, _owner.Foreground, pixelsPerDip);

            drawingContext.PushOpacity(0.55);
            for (var i = 0; i < _owner.Text.Length; i++)
            {
                if (_owner.Text[i] == '\n' && i > 0 && _owner.Text[i - 1] == '\r') continue;
                if (_owner.Text[i] != '\n' && _owner.Text[i] != '\r') continue;

                var anchorIndex = i > 0 && _owner.Text[i - 1] != '\r' ? i - 1 : i;
                var rect = _owner.GetRectFromCharacterIndex(anchorIndex, true);
                if (rect.IsEmpty) rect = _owner.GetRectFromCharacterIndex(i, false);
                if (rect.IsEmpty) continue;

                var point = new WpfPoint(rect.Right + 1, rect.Top + (rect.Height - marker.Height) / 2);
                drawingContext.DrawText(marker, point);
            }

            drawingContext.Pop();
        }
    }
}
