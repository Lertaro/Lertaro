using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Brush = System.Windows.Media.Brush;
using Pen = System.Windows.Media.Pen;

namespace Lertaro.App.Helpers;

// Draws a brief accent-colored outline around a settings control after search navigates to it,
// mirroring Windows 11 Settings' "flash the matched control" behavior. Uses an Adorner rather than
// wrapping every settings row in a Border, since the target can be any control type (CheckBox, Grid,
// Button...) and this way none of the existing settings XAML needs restructuring -- SettingsWindow.xaml
// only needs an AdornerDecorator somewhere above the settings pages for GetAdornerLayer to find.
public static class SettingsSearchHighlight
{
    public static void Show(FrameworkElement target)
    {
        var layer = AdornerLayer.GetAdornerLayer(target);
        if (layer == null)
            return;

        var brush = target.TryFindResource("AccentBlue") as Brush ?? System.Windows.Media.Brushes.DodgerBlue;
        var adorner = new FlashAdorner(target, brush);
        layer.Add(adorner);

        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(900))
        {
            BeginTime = TimeSpan.FromMilliseconds(700),
        };
        fade.Completed += (_, _) => layer.Remove(adorner);
        adorner.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    private sealed class FlashAdorner : Adorner
    {
        private readonly Pen _pen;

        public FlashAdorner(UIElement adornedElement, Brush brush) : base(adornedElement)
        {
            _pen = new Pen(brush, 2);
            IsHitTestVisible = false;
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            // Flush with the target's own bounds, not outset: several jump targets (e.g. the
            // Local/Network/Folders index cards, styled via "SettingsCard") span the full width of
            // their scrollable container with zero horizontal margin, so an outward-inflated border has
            // nowhere to go and gets clipped by whichever ancestor happens to own that space
            // (ScrollViewer, AdornerDecorator, ...). Drawing exactly on the target's own bounds needs no
            // borrowed space, so it renders complete regardless of how tightly the target fits its
            // container.
            var rect = new Rect(AdornedElement.RenderSize);
            if (rect.Width <= 0 || rect.Height <= 0)
                return;
            drawingContext.DrawRoundedRectangle(null, _pen, rect, 6, 6);
        }
    }
}
