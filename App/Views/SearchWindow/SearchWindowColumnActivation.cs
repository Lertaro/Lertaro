using System.IO;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows;
using Lertaro.App.Services;
using Lertaro.App.Services.Plugin;
using Lertaro.App.Helpers.Visuals;
using ListViewItem = System.Windows.Controls.ListViewItem;

namespace Lertaro.App.Views.SearchWindow;

// Split out purely to keep SearchWindowInputHandler under the repo's per-file line limit; this class
// owns only full-window column activation and always operates on the one SearchWindow passed to it.
internal sealed class SearchWindowColumnActivation(Lertaro.App.SearchWindow window)
{
    public bool TryHandle(MouseButtonEventArgs e, ListViewItem item, AppSearchResult result, bool isFileOrFolder)
    {
        var columnId = GetClickedColumnId(e, item);
        if (string.IsNullOrEmpty(columnId))
            return false;

        foreach (var provider in PluginManager.Instance.ResultColumnProviders)
        {
            foreach (var column in provider.GetColumns())
            {
                if (column.ColumnId == columnId && column.OnDoubleClick != null)
                {
                    column.OnDoubleClick(result);
                    return true;
                }
            }
        }

        if (columnId != "Path" || !isFileOrFolder)
            return false;

        FileExecutor.LocateInExplorer(result.FullPath);
        return true;
    }

    internal static bool IsFileOrFolder(AppSearchResult result)
    {
        if (result.IsSearchSectionHeader || result.IsEmptyResult)
            return false;
        if (result.ResultKind is "File" or "Folder" or "Directory")
            return true;

        return result.IsDir ? Directory.Exists(result.FullPath) : File.Exists(result.FullPath);
    }

    // Mouse events can originate from a non-Visual ContentElement such as a highlighted Run.
    internal static DependencyObject? VisualOrContentParent(DependencyObject value) =>
        value is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D
            ? System.Windows.Media.VisualTreeHelper.GetParent(value)
            : (value as FrameworkContentElement)?.Parent;

    private string? GetClickedColumnId(MouseButtonEventArgs e, ListViewItem item)
    {
        if (window.LstGridResultsControl.View is not GridView gridView)
            return null;

        var columns = gridView.Columns.Cast<GridViewColumn>()
            .Select(column => (ColumnIdentity.GetId(column), column.ActualWidth));
        return ResolveColumnIdAtX(e.GetPosition(item).X, columns);
    }

    internal static string? ResolveColumnIdAtX(double x, IEnumerable<(string ColumnId, double Width)> columns)
    {
        double cumulativeWidth = 0;
        foreach (var (columnId, width) in columns)
        {
            cumulativeWidth += width;
            if (x < cumulativeWidth)
                return columnId;
        }

        return null;
    }
}
