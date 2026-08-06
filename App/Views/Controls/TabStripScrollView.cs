using System.Windows;
using System.Windows.Controls;
using Button = System.Windows.Controls.Button;

namespace Lertaro.App.Views.Controls;

// Wraps a horizontal tab-header strip (the Settings window's per-page sub-tab row) in a horizontally
// scrolling viewport with Chrome-style overflow arrows -- English (or any longer-than-Chinese) tab
// labels can add up to more than the ~500px content pane the Settings window gives each page, and the
// page's own outer ScrollViewer only scrolls vertically, so without this the tail end of the strip was
// silently clipped with no way to reach it. The arrows only appear once there's actually something to
// scroll to in that direction; when everything fits (most languages, most window widths) both stay
// collapsed and this behaves like a plain content host.
public class TabStripScrollView : ContentControl
{
    private const double ScrollIncrement = 150;

    private ScrollViewer? _scroller;
    private Button? _leftButton;
    private Button? _rightButton;

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        _scroller?.ScrollChanged -= Scroller_ScrollChanged;
        _leftButton?.Click -= LeftButton_Click;
        _rightButton?.Click -= RightButton_Click;

        _scroller = GetTemplateChild("PART_Scroller") as ScrollViewer;
        _leftButton = GetTemplateChild("PART_LeftButton") as Button;
        _rightButton = GetTemplateChild("PART_RightButton") as Button;

        _scroller?.ScrollChanged += Scroller_ScrollChanged;
        _leftButton?.Click += LeftButton_Click;
        _rightButton?.Click += RightButton_Click;

        UpdateArrowVisibility();
    }

    private void LeftButton_Click(object sender, RoutedEventArgs e) => Scroll(-ScrollIncrement);
    private void RightButton_Click(object sender, RoutedEventArgs e) => Scroll(ScrollIncrement);

    private void Scroll(double delta)
    {
        if (_scroller == null) return;
        var target = Math.Clamp(_scroller.HorizontalOffset + delta, 0, _scroller.ScrollableWidth);
        _scroller.ScrollToHorizontalOffset(target);
    }

    private void Scroller_ScrollChanged(object sender, ScrollChangedEventArgs e) => UpdateArrowVisibility();

    private void UpdateArrowVisibility()
    {
        if (_scroller == null) return;
        _leftButton?.Visibility = _scroller.HorizontalOffset > 0.5 ? Visibility.Visible : Visibility.Collapsed;
        _rightButton?.Visibility = _scroller.HorizontalOffset < _scroller.ScrollableWidth - 0.5 ? Visibility.Visible : Visibility.Collapsed;
    }
}
