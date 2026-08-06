using System.Windows;
using System.Windows.Controls;
using Lertaro.App.Helpers.Visuals;
using Lertaro.App.Services;
using Lertaro.Core;

using Lertaro.App.Services.Plugin;
namespace Lertaro.App.Views.Controls.Results;

// Grid-mode (full/main window) dynamic column population and header-click sorting -- split out of
// ResultsControl.xaml.cs to keep that file under the project's line limit. Unrelated to the shared
// list-mode result list (quick/inline windows) the rest of that file deals with.
internal static class ResultsControlColumns
{
    public static void PopulateDynamicColumns(System.Windows.Controls.ListView lstGridResults)
    {
        if (lstGridResults.View is not GridView gridView) return;

        foreach (var provider in PluginManager.Instance.ResultColumnProviders)
        {
            foreach (var colDef in provider.GetColumns())
            {
                var gvc = new GridViewColumn
                {
                    Header = colDef.HeaderText,
                    Width = colDef.Width
                };
                ColumnIdentity.SetId(gvc, colDef.ColumnId);

                var binding = new System.Windows.Data.Binding($"[{colDef.ColumnId}]")
                {
                    Mode = System.Windows.Data.BindingMode.OneWay
                };
                var textBlockFactory = new FrameworkElementFactory(typeof(TextBlock));
                textBlockFactory.SetBinding(TextBlock.TextProperty, binding);
                textBlockFactory.SetValue(TextBlock.ForegroundProperty, new DynamicResourceExtension("TextSecondary2"));
                textBlockFactory.SetValue(TextBlock.FontSizeProperty, 12.0);
                textBlockFactory.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
                // A TextBlock with no Background is only hit-testable over its rendered glyphs (WPF's
                // usual "empty space in an unpainted element passes mouse input through" rule), which is
                // why hovering past the end of a short value (or above/below it) swallowed the mouse
                // wheel instead of scrolling the list -- see the matching fix in ResultsControl.xaml.
                textBlockFactory.SetValue(TextBlock.BackgroundProperty, System.Windows.Media.Brushes.Transparent);
                textBlockFactory.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Stretch);
                gvc.CellTemplate = new DataTemplate { VisualTree = textBlockFactory };
                gridView.Columns.Add(gvc);
            }
        }
    }

    // Reorders the already-populated columns (built-in + plugin) to match UserSettings.ColumnOrder --
    // a column whose id isn't listed yet keeps its natural position, same "unlisted falls back to
    // int.MaxValue, stable sort preserves relative order" convention as SidebarGroupOrder/
    // ResultTypeOrder/QuickNavigationProviderOrder. Purely a display-position concern, unrelated to
    // which column the rows are sorted BY (see SearchResultSortMemory).
    public static void ApplyColumnOrder(System.Windows.Controls.ListView lstGridResults, List<string> order)
    {
        if (order.Count == 0) return;
        if (lstGridResults.View is not GridView gridView) return;

        var target = gridView.Columns
            .Select((col, index) => (col, index))
            .OrderBy(x =>
            {
                var rank = order.IndexOf(ColumnIdentity.GetId(x.col));
                return rank >= 0 ? rank : int.MaxValue;
            })
            .ThenBy(x => x.index)
            .Select(x => x.col)
            .ToList();

        for (var i = 0; i < target.Count; i++)
        {
            var currentIndex = gridView.Columns.IndexOf(target[i]);
            if (currentIndex != i) gridView.Columns.Move(currentIndex, i);
        }
    }

    // Single source of truth for a column's CURRENT, correctly-translated header text, resolved fresh
    // from TranslationManager/PluginManager every time rather than read back off the GridViewColumn
    // itself -- col.Header stops being a reliable source the moment any of the methods below have
    // overwritten it once (see their own comments), so re-deriving it from the id avoids ever
    // compounding a stale value forward.
    private static string ResolveFreshHeaderText(string columnId) => columnId switch
    {
        "Name" => TranslationManager.Instance["Search_HeaderName"],
        "Path" => TranslationManager.Instance["Search_HeaderPath"],
        "DateModified" => TranslationManager.Instance["Search_HeaderDateModified"],
        _ => PluginManager.Instance.ResultColumnProviders
                 .SelectMany(p => p.GetColumns())
                 .FirstOrDefault(c => c.ColumnId == columnId)?.HeaderText ?? columnId
    };

    // Re-resolves every column's header text (built-in and plugin alike) in the now-current language and
    // re-applies it in place -- called on TranslationManager language switches. The 3 built-in columns'
    // XAML Header bindings only cover the FIRST render: the moment HandleColumnHeaderClick/
    // ApplyInitialSortIndicator below assigns col.Header a literal string (to paint the sort arrow), that
    // binding is gone and the column would otherwise stay stuck in whatever language was active at that
    // instant. Driving every column off ColumnIdentity.Id here instead of position also means an
    // enabled/disabled plugin mid-session can never relabel the wrong column. Preserves an existing
    // sort-arrow suffix so an active sort indicator survives the relabel.
    public static void RefreshAllColumnHeaders(System.Windows.Controls.ListView lstGridResults)
    {
        if (lstGridResults.View is not GridView gridView) return;

        foreach (var col in gridView.Columns)
        {
            var id = ColumnIdentity.GetId(col);
            if (string.IsNullOrEmpty(id) || col.Header is not string current) continue;

            var suffix = current.EndsWith(" ▲") ? " ▲" : current.EndsWith(" ▼") ? " ▼" : string.Empty;
            col.Header = ResolveFreshHeaderText(id) + suffix;
        }
    }

    // Paints the sort-arrow indicator for whatever column the ViewModel's own default sort (General
    // settings -> Search Window tab) already applied to the results -- otherwise a window that opens
    // pre-sorted would show correctly ordered rows with no header hinting why, until the user's first
    // manual click. Safe no-op for any DataContext without a CurrentSortColumn/IsSortAscending pair
    // (list-mode-only owners like QuickSearchWindow/InlineSearchWindow, which still run this on Loaded).
    public static void ApplyInitialSortIndicator(System.Windows.Controls.ListView lstGridResults, object? dataContext)
    {
        if (dataContext == null || lstGridResults.View is not GridView gridView) return;

        try
        {
            dynamic vm = dataContext;
            string columnId = vm.CurrentSortColumn;
            if (string.IsNullOrEmpty(columnId)) return;
            bool isAsc = vm.IsSortAscending;

            foreach (var col in gridView.Columns)
            {
                var id = ColumnIdentity.GetId(col);
                if (id != columnId) continue;
                col.Header = ResolveFreshHeaderText(id) + (isAsc ? " ▲" : " ▼");
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"[ResultsControlColumns] ApplyInitialSortIndicator failed: {ex}", LogLevel.Error);
        }
    }

    public static void HandleColumnHeaderClick(GridViewColumnHeader? headerClicked, object? dataContext, System.Windows.Controls.ListView lstGridResults)
    {
        // Null, or missing a Column, whenever the click resolved to something other than a header cell
        // (e.g. the resize gripper) -- not an error, just nothing to sort by.
        if (headerClicked is not { Column: not null })
            return;

        var columnId = ColumnIdentity.GetId(headerClicked.Column);
        if (string.IsNullOrEmpty(columnId) || dataContext == null)
            return;

        dynamic vm = dataContext;
        try
        {
            vm.SortByColumn(columnId);
            // Reads CurrentSortColumn back rather than assuming it's still `columnId`: a third click on
            // the same column resets SortByColumn's own state back to the default relevance order, and
            // the header repaint below needs to reflect THAT (no arrow anywhere), not "the column that
            // was just clicked" -- which is what would otherwise incorrectly keep painting an arrow on it.
            string currentSortColumn = vm.CurrentSortColumn;
            bool isAsc = vm.IsSortAscending;

            if (lstGridResults.View is not GridView gridView) return;

            foreach (var col in gridView.Columns)
            {
                var id = ColumnIdentity.GetId(col);
                if (string.IsNullOrEmpty(id)) continue;

                col.Header = id == currentSortColumn
                    ? ResolveFreshHeaderText(id) + (isAsc ? " ▲" : " ▼")
                    : ResolveFreshHeaderText(id);
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"[ResultsControlColumns] HandleColumnHeaderClick failed for column '{columnId}': {ex}", LogLevel.Error);
        }
    }
}
