using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Lertaro.App.Views.InlineSearchWindow.Helpers;

/// <summary>
/// Gives each inline result row its own name column. The row's widest name is capped at 70 percent,
/// leaving the rest of that same row for its path.
/// </summary>
public static class InlineResultColumnsBehavior
{
    public static readonly DependencyProperty IsResultGridProperty =
        DependencyProperty.RegisterAttached("IsResultGrid", typeof(bool), typeof(InlineResultColumnsBehavior),
            new PropertyMetadata(false, OnIsResultGridChanged));

    private const double MaximumNameRatio = 0.7;

    public static bool GetIsResultGrid(DependencyObject obj) => (bool)obj.GetValue(IsResultGridProperty);
    public static void SetIsResultGrid(DependencyObject obj, bool value) => obj.SetValue(IsResultGridProperty, value);

    private static void OnIsResultGridChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Grid grid) return;
        DetachGrid(grid);
        grid.Loaded -= GridLoaded;
        grid.Unloaded -= GridUnloaded;
        if ((bool)e.NewValue)
        {
            grid.Loaded += GridLoaded;
            grid.Unloaded += GridUnloaded;
            if (grid.IsLoaded) AttachGrid(grid);
        }
    }

    private static void GridLoaded(object sender, RoutedEventArgs e) => AttachGrid((Grid)sender);
    private static void GridUnloaded(object sender, RoutedEventArgs e) => DetachGrid((Grid)sender);
    private static void GridDataContextChanged(object sender, DependencyPropertyChangedEventArgs e) => QueueRefresh((Grid)sender);
    private static void GridSizeChanged(object sender, SizeChangedEventArgs e) => Refresh((Grid)sender);

    private static void AttachGrid(Grid grid)
    {
        DetachGrid(grid);
        grid.DataContextChanged += GridDataContextChanged;
        grid.SizeChanged += GridSizeChanged;
        Refresh(grid);
    }

    private static void DetachGrid(Grid grid)
    {
        grid.DataContextChanged -= GridDataContextChanged;
        grid.SizeChanged -= GridSizeChanged;
    }

    private static void QueueRefresh(Grid grid)
        => grid.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => Refresh(grid)));

    private static void Refresh(Grid grid)
    {
        if (grid.ColumnDefinitions.Count < 2 || grid.ActualWidth <= 0)
            return;

        var sample = FindDescendant<TextBlock>(grid);
        if (sample == null || sample.FontSize <= 0)
            return;

        var row = sample.DataContext as AppSearchResult ?? grid.DataContext as AppSearchResult;
        if (row == null || row.IsListItem || row.IsEmptyResult || row.IsSearchSectionHeader)
            return;

        var text = row.IsJumpToExplorerPath ? row.ParentDir : row.Name;
        var nameWidth = Math.Min(InlineTextMetrics.Measure(text, sample), grid.ActualWidth * MaximumNameRatio);
        grid.ColumnDefinitions[0].Width = new GridLength(nameWidth, GridUnitType.Pixel);
        grid.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Star);
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) return match;
            if (FindDescendant<T>(child) is { } nested) return nested;
        }

        return null;
    }
}
