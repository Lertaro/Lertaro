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
        MaterializeApproachingGroups(groups);
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

    // The panel deliberately has one outer scroller so group headers and rows move together. That
    // prevents WPF's built-in ListBox virtualization from owning each nested list, so groups expose a
    // bounded page and this outer viewport asks for the next one only when the user approaches it.
    private void MaterializeApproachingGroups(IEnumerable<Expander> groups)
    {
        foreach (var expander in groups)
        {
            if (!expander.IsExpanded || expander.DataContext is not QuickPanelGroupViewModel group)
                continue;

            var bounds = expander.TransformToAncestor(_scrollViewer)
                .TransformBounds(new Rect(new System.Windows.Point(), expander.RenderSize));
            if (bounds.Bottom >= 0 && bounds.Bottom <= _scrollViewer.ViewportHeight + 240 && group.LoadNextPage())
                return;
        }
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
