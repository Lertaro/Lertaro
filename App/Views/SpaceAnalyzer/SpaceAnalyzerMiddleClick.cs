using System.Windows;
using System.Windows.Input;
using Lertaro.App.Services;
using Lertaro.App.ViewModels.SpaceAnalyzer;
using ItemsControl = System.Windows.Controls.ItemsControl;
using ListBox = System.Windows.Controls.ListBox;
using ListBoxItem = System.Windows.Controls.ListBoxItem;

namespace Lertaro.App.Views.SpaceAnalyzer;

// Split out to keep SpaceAnalyzerView below the repository's per-file line limit. This helper owns
// only the shared middle-click behavior used by the list and treemap.
internal static class SpaceAnalyzerMiddleClick
{
    public static void Attach(ListBox list) => list.MouseDown += (_, e) => HandleListMouseDown(list, e);

    public static void Locate(SpaceDisplayItem item) => FileExecutor.LocateInExplorer(item.Path);

    private static void HandleListMouseDown(ListBox list, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle)
            return;

        var row = ItemsControl.ContainerFromElement(list, e.OriginalSource as DependencyObject) as ListBoxItem;
        if (row?.Content is not SpaceDisplayItem item)
            return;

        list.SelectedItem = item;
        Locate(item);
        e.Handled = true;
    }
}
