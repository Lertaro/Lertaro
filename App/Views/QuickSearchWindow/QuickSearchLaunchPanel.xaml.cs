using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Lertaro.App.Helpers.Visuals;
using Lertaro.App.Services.AppWindow;
using Lertaro.App.ViewModels.Search;
using ContextMenu = System.Windows.Controls.ContextMenu;
using MenuItem = System.Windows.Controls.MenuItem;
using WpfUserControl = System.Windows.Controls.UserControl;
using WpfDataFormats = System.Windows.DataFormats;
using WpfDragDropEffects = System.Windows.DragDropEffects;
using WpfDragEventArgs = System.Windows.DragEventArgs;
using ResultsDragDropHelper = Lertaro.App.Views.Controls.Results.ResultsDragDropHelper;
using WpfButtonBase = System.Windows.Controls.Primitives.ButtonBase;

namespace Lertaro.App.Views.QuickSearchWindow;

public partial class QuickSearchLaunchPanel : WpfUserControl
{
    private const double SourceSlotWidth = 32;
    private DispatcherTimer? _sourceRevealTimer;
    private int _sourceRevealGeneration;

    public QuickSearchLaunchPanel() => InitializeComponent();

    internal void ScrollSelectedItemIntoView(AppSearchResult? item)
    {
        if (item != null)
            LaunchItemsListView.ScrollIntoView(item);
    }

    internal void SetActionsModeHeight(bool expanded)
    {
        if (expanded)
        {
            Height = Services.UiMetrics.ScaledQuickSearchMaxResultHeight;
            return;
        }

        ClearValue(HeightProperty);
    }

    private void Panel_PreviewDragOver(object sender, WpfDragEventArgs e)
    {
        if (!e.Data.GetDataPresent(WpfDataFormats.FileDrop))
            return;

        if (CanAcceptFileDrop(e))
        {
            e.Effects = WpfDragDropEffects.Copy;
            e.Handled = true;
            return;
        }

        e.Effects = WpfDragDropEffects.None;
        e.Handled = true;
    }

    private void Panel_PreviewDrop(object sender, WpfDragEventArgs e)
    {
        if (!e.Data.GetDataPresent(WpfDataFormats.FileDrop))
            return;

        if (CanAcceptFileDrop(e) && e.Data.GetData(WpfDataFormats.FileDrop) is string[] paths
            && DataContext is QuickSearchViewModel viewModel)
        {
            viewModel.AddLaunchPanelDroppedPaths(paths);
        }

        e.Handled = true;
    }

    private bool CanAcceptFileDrop(WpfDragEventArgs e)
        => e.Data.GetDataPresent(WpfDataFormats.FileDrop)
            && !ResultsDragDropHelper.IsDragActive
            && DataContext is QuickSearchViewModel { CanAcceptLaunchPanelDrops: true };

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
        if (sender is not System.Windows.Controls.Button button)
            return;

        ++_sourceRevealGeneration;
        _sourceRevealTimer?.Stop();
        _sourceRevealTimer = null;
        ResetSourceButtons();
        AnimateSourceExpand(button);
    }

    private void SourceButton_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button button)
            return;

        ++_sourceRevealGeneration;
        _sourceRevealTimer?.Stop();
        _sourceRevealTimer = null;
        AnimateSourceCollapse(button);
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
            if (button == null) return;

            AnimateSourceExpand(button);

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
                if (!button.IsMouseOver) AnimateSourceCollapse(button);
            };
            timer.Start();
        });
    }

    private static void AnimateSourceExpand(System.Windows.Controls.Button button)
    {
        var slot = VisualTreeHelper.GetParent(button) as System.Windows.Controls.Grid;
        var reveal = slot?.Children.OfType<System.Windows.Controls.Canvas>()
            .FirstOrDefault(candidate => candidate.Name == "SourceReveal");
        var name = reveal?.Children.OfType<System.Windows.Controls.TextBlock>().FirstOrDefault();
        if (name == null) return;

        name.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
        var expandedWidth = Math.Max(
            SourceSlotWidth,
            Math.Ceiling(name.DesiredSize.Width + name.Margin.Left + name.Margin.Right + 6));
        if (slot != null) System.Windows.Controls.Panel.SetZIndex(slot, 100);
        var duration = new Duration(TimeSpan.FromMilliseconds(160));
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        slot?.BeginAnimation(WidthProperty,
            new DoubleAnimation(SourceSlotWidth, expandedWidth, duration) { EasingFunction = easing });
        button.BeginAnimation(WidthProperty,
            new DoubleAnimation(SourceSlotWidth, expandedWidth, duration) { EasingFunction = easing });
        (button.Content as System.Windows.Controls.Grid)?.Children
            .OfType<System.Windows.Controls.StackPanel>().FirstOrDefault()?.BeginAnimation(
            OpacityProperty, new DoubleAnimation(1, 0, duration));
        name.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration));
    }

    private void ResetSourceButtons()
    {
        foreach (var button in FindVisualChildren<System.Windows.Controls.Button>(this)
                     .Where(candidate => candidate.DataContext is LaunchPanelSourceViewModel))
        {
            var slot = VisualTreeHelper.GetParent(button) as System.Windows.Controls.Grid;
            if (slot != null) System.Windows.Controls.Panel.SetZIndex(slot, 0);
            slot?.BeginAnimation(WidthProperty, null);
            slot?.Width = SourceSlotWidth;
            button.BeginAnimation(WidthProperty, null);
            button.Width = SourceSlotWidth;
            var reveal = slot?.Children.OfType<System.Windows.Controls.Canvas>()
                .FirstOrDefault(candidate => candidate.Name == "SourceReveal");
            var dots = (button.Content as System.Windows.Controls.Grid)?.Children
                .OfType<System.Windows.Controls.StackPanel>().FirstOrDefault();
            dots?.BeginAnimation(OpacityProperty, null);
            dots?.Opacity = 1;
            var name = reveal?.Children.OfType<System.Windows.Controls.TextBlock>().FirstOrDefault();
            name?.BeginAnimation(OpacityProperty, null);
            name?.Opacity = 0;
        }
    }

    private static void AnimateSourceCollapse(System.Windows.Controls.Button button)
    {
        var duration = new Duration(TimeSpan.FromMilliseconds(160));
        var slot = VisualTreeHelper.GetParent(button) as System.Windows.Controls.Grid;
        if (slot != null) System.Windows.Controls.Panel.SetZIndex(slot, 0);
        slot?.BeginAnimation(WidthProperty,
            new DoubleAnimation { To = SourceSlotWidth, Duration = duration });
        button.BeginAnimation(WidthProperty,
            new DoubleAnimation { To = SourceSlotWidth, Duration = duration });
        var reveal = slot?.Children.OfType<System.Windows.Controls.Canvas>()
            .FirstOrDefault(candidate => candidate.Name == "SourceReveal");
        (button.Content as System.Windows.Controls.Grid)?.Children
            .OfType<System.Windows.Controls.StackPanel>().FirstOrDefault()?.BeginAnimation(
            OpacityProperty, new DoubleAnimation { To = 1, Duration = duration });
        reveal?.Children.OfType<System.Windows.Controls.TextBlock>().FirstOrDefault()?.BeginAnimation(
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

    private void LaunchItem_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (IsInsideButton(e.OriginalSource as DependencyObject)
            || sender is not FrameworkElement { DataContext: AppSearchResult result })
            return;
        if (Window.GetWindow(this) is not Lertaro.App.QuickSearchWindow window) return;

        e.Handled = true;
        window.ExecuteFavorite(result);
    }

    private void LaunchItemMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button button || button.ContextMenu == null)
            return;

        button.ContextMenu.PlacementTarget = button;
        button.ContextMenu.IsOpen = true;
    }

    private void EditLaunchItem_Click(object sender, RoutedEventArgs e)
    {
        if (GetContextMenuResult(sender) is { } result)
            AppWindowManager.ShowQuickLaunchItemEditor(result.FullPath);

        e.Handled = true;
    }

    private void DeleteLaunchItem_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is QuickSearchViewModel viewModel && GetContextMenuResult(sender) is { } result)
            viewModel.RemoveLaunchPanelItem(result);

        e.Handled = true;
    }

    private static AppSearchResult? GetContextMenuResult(object sender)
    {
        if (sender is not MenuItem menuItem || menuItem.Parent is not ContextMenu menu)
            return null;

        return (menu.PlacementTarget as FrameworkElement)?.DataContext as AppSearchResult;
    }

    private void LaunchItem_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (IsInsideButton(e.OriginalSource as DependencyObject)
            || sender is not FrameworkElement { DataContext: AppSearchResult result })
            return;

        if (Window.GetWindow(this) is not Lertaro.App.QuickSearchWindow window)
            return;

        window.EnterLaunchPanelActions(result);
        e.Handled = true;
    }

    private static bool IsInsideButton(DependencyObject? source)
    {
        for (var node = source; node != null; node = TreeWalk.Parent(node))
        {
            if (node is WpfButtonBase)
                return true;
        }

        return false;
    }
}
