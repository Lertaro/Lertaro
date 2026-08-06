using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Lertaro.App.ViewModels.QuickPanel;

namespace Lertaro.App.Views.QuickPanel;

// Split from the window to keep its input-handling file under the repository line limit. This helper
// owns only the sticky-header presentation state; group preferences remain on their view models.
internal sealed class QuickPanelScrollSupport
{
    private readonly ScrollViewer _scrollViewer;
    private readonly ContentControl _stickyHeader;

    public QuickPanelScrollSupport(ScrollViewer scrollViewer, ContentControl stickyHeader)
    {
        _scrollViewer = scrollViewer;
        _stickyHeader = stickyHeader;
        _scrollViewer.ScrollChanged += (_, _) => Update();
        _scrollViewer.SizeChanged += (_, _) => Update();
    }

    public void Update()
    {
        var groups = FindVisualChildren<Expander>(_scrollViewer)
            .Where(expander => expander.DataContext is QuickPanelGroupViewModel)
            .ToList();
        if (groups.Count < 2)
        {
            HideStickyHeader();
            return;
        }

        QuickPanelGroupViewModel? stickyGroup = null;
        foreach (var expander in groups)
        {
            if (expander.DataContext is not QuickPanelGroupViewModel group || !expander.IsVisible) continue;

            var bounds = expander.TransformToAncestor(_scrollViewer)
                .TransformBounds(new Rect(new System.Windows.Point(), expander.RenderSize));
            if (bounds.Bottom <= 0)
            {
                stickyGroup = group;
                continue;
            }

            if (bounds.Top < 0) stickyGroup = group;
        }

        if (stickyGroup == null)
        {
            HideStickyHeader();
            return;
        }

        _stickyHeader.Content = stickyGroup;
        _stickyHeader.Visibility = Visibility.Visible;
    }

    private void HideStickyHeader()
    {
        _stickyHeader.Content = null;
        _stickyHeader.Visibility = Visibility.Collapsed;
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T typed) yield return typed;
            foreach (var descendant in FindVisualChildren<T>(child)) yield return descendant;
        }
    }
}
