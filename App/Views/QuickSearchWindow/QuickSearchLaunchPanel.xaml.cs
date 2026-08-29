using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Lertaro.App.ViewModels.Search;
using WpfUserControl = System.Windows.Controls.UserControl;

namespace Lertaro.App.Views.QuickSearchWindow;

public partial class QuickSearchLaunchPanel : WpfUserControl
{
    private DispatcherTimer? _sourceRevealTimer;
    private int _sourceRevealGeneration;

    public QuickSearchLaunchPanel() => InitializeComponent();

    private void Panel_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Shift) == 0) return;
        if (DataContext is not QuickSearchViewModel viewModel) return;

        viewModel.CycleLaunchSource(e.Delta > 0 ? -1 : 1);
        PlaySelectedSourceReveal(viewModel);
        e.Handled = true;
    }

    private void SourceButton_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button button || button.Content is not System.Windows.Controls.Grid grid)
            return;

        ++_sourceRevealGeneration;
        _sourceRevealTimer?.Stop();
        _sourceRevealTimer = null;
        ResetSourceButtons();
        AnimateSourceExpand(button, grid);
    }

    private void SourceButton_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button button || button.Content is not System.Windows.Controls.Grid grid)
            return;

        ++_sourceRevealGeneration;
        _sourceRevealTimer?.Stop();
        _sourceRevealTimer = null;
        AnimateSourceCollapse(button, grid);
    }

    private void PlaySelectedSourceReveal(QuickSearchViewModel viewModel)
    {
        var generation = ++_sourceRevealGeneration;
        _sourceRevealTimer?.Stop();
        _sourceRevealTimer = null;
        ResetSourceButtons();

        Dispatcher.BeginInvoke(DispatcherPriority.Render, () =>
        {
            if (generation != _sourceRevealGeneration) return;

            var selected = viewModel.SelectedLaunchSource;
            var button = FindVisualChildren<System.Windows.Controls.Button>(this)
                .FirstOrDefault(candidate => ReferenceEquals(candidate.DataContext, selected));
            if (button?.Content is not System.Windows.Controls.Grid grid) return;

            AnimateSourceExpand(button, grid);

            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            _sourceRevealTimer = timer;
            timer.Tick += (_, _) =>
            {
                if (generation != _sourceRevealGeneration)
                {
                    timer.Stop();
                    return;
                }
                timer.Stop();
                if (ReferenceEquals(_sourceRevealTimer, timer)) _sourceRevealTimer = null;
                if (!button.IsMouseOver) AnimateSourceCollapse(button, grid);
            };
            timer.Start();
        });
    }

    private static void AnimateSourceExpand(System.Windows.Controls.Button button,
        System.Windows.Controls.Grid grid)
    {
        var name = grid.Children.OfType<System.Windows.Controls.TextBlock>().FirstOrDefault();
        if (name == null) return;

        var textWidth = name.ActualWidth > 0 ? name.ActualWidth : name.DesiredSize.Width;
        var expandedWidth = Math.Clamp(Math.Ceiling(textWidth + name.Margin.Left + 6), 18, 136);
        var slot = VisualTreeHelper.GetParent(button) as System.Windows.Controls.Grid;
        var duration = new Duration(TimeSpan.FromMilliseconds(160));
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        slot?.BeginAnimation(WidthProperty,
            new DoubleAnimation(18, expandedWidth, duration) { EasingFunction = easing });
        button.BeginAnimation(WidthProperty,
            new DoubleAnimation(18, expandedWidth, duration) { EasingFunction = easing });
        grid.Children.OfType<System.Windows.Controls.StackPanel>().FirstOrDefault()?.BeginAnimation(
            OpacityProperty, new DoubleAnimation(1, 0, duration));
        name.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration));
    }

    private void ResetSourceButtons()
    {
        foreach (var button in FindVisualChildren<System.Windows.Controls.Button>(this)
                     .Where(candidate => candidate.DataContext is LaunchPanelSourceViewModel))
        {
            var slot = VisualTreeHelper.GetParent(button) as System.Windows.Controls.Grid;
            slot?.BeginAnimation(WidthProperty, null);
            slot?.Width = 18;
            button.BeginAnimation(WidthProperty, null);
            button.Width = 18;
            if (button.Content is not System.Windows.Controls.Grid grid) continue;

            var dots = grid.Children.OfType<System.Windows.Controls.StackPanel>().FirstOrDefault();
            dots?.BeginAnimation(OpacityProperty, null);
            dots?.Opacity = 1;
            var name = grid.Children.OfType<System.Windows.Controls.TextBlock>().FirstOrDefault();
            name?.BeginAnimation(OpacityProperty, null);
            name?.Opacity = 0;
        }
    }

    private static void AnimateSourceCollapse(System.Windows.Controls.Button button,
        System.Windows.Controls.Grid grid)
    {
        var duration = new Duration(TimeSpan.FromMilliseconds(160));
        var slot = VisualTreeHelper.GetParent(button) as System.Windows.Controls.Grid;
        slot?.BeginAnimation(WidthProperty, new DoubleAnimation { To = 18, Duration = duration });
        button.BeginAnimation(WidthProperty, new DoubleAnimation { To = 18, Duration = duration });
        grid.Children.OfType<System.Windows.Controls.StackPanel>().FirstOrDefault()?.BeginAnimation(
            OpacityProperty, new DoubleAnimation { To = 1, Duration = duration });
        grid.Children.OfType<System.Windows.Controls.TextBlock>().FirstOrDefault()?.BeginAnimation(
            OpacityProperty, new DoubleAnimation { To = 0, Duration = duration });
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (var descendant in FindVisualChildren<T>(child)) yield return descendant;
        }
    }

    private void LaunchButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: AppSearchResult result }) return;
        if (Window.GetWindow(this) is not Lertaro.App.QuickSearchWindow window) return;

        e.Handled = true;
        window.ExecuteFavorite(result);
    }
}
