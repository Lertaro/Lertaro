using System.Windows;
using Lertaro.App.Services;
using Lertaro.App.Views.Controls.Results;
using ListBox = System.Windows.Controls.ListBox;
using ListBoxItem = System.Windows.Controls.ListBoxItem;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace Lertaro.App;

// Keeps hover-only preview updates out of SearchWindow's already large event-wiring class. Hovering
// changes the preview target, but deliberately never changes the ListView's actual selection.
internal sealed class SearchWindowHoverPreviewSupport
{
    private readonly SearchWindow _window;
    private string? _lastHoveredPreviewPath;

    public SearchWindowHoverPreviewSupport(SearchWindow window, ListBox list)
    {
        _window = window;
        list.MouseMove += OnListMouseMove;
    }

    private void OnListMouseMove(object sender, MouseEventArgs e)
    {
        var item = ResultsControl.FindVisualParent<ListBoxItem>(e.OriginalSource as DependencyObject);
        if (item?.Content is not AppSearchResult result || !result.CanPreview)
        {
            _lastHoveredPreviewPath = null;
            return;
        }

        if (string.Equals(_lastHoveredPreviewPath, result.FullPath, StringComparison.OrdinalIgnoreCase))
            return;

        _lastHoveredPreviewPath = result.FullPath;
        QuickLookManager.Instance.UpdateOrShow(_window, result.FullPath);
    }
}
