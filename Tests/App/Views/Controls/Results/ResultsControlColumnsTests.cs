using System.Windows.Controls;
using Lertaro.App.Helpers.Visuals;
using Lertaro.App.Views.Controls.Results;

namespace Lertaro.App.Tests.Views.Controls.Results;

[TestClass]
public sealed class ResultsControlColumnsTests
{
    private static ListView MakeGridListView(params string[] columnIds)
    {
        var gridView = new GridView();
        foreach (var id in columnIds)
        {
            var column = new GridViewColumn { Header = id };
            ColumnIdentity.SetId(column, id);
            gridView.Columns.Add(column);
        }
        return new ListView { View = gridView };
    }

    private static List<string> ColumnIds(ListView lstGridResults) =>
        ((GridView)lstGridResults.View).Columns.Select(c => ColumnIdentity.GetId(c)).ToList();

    [StaTestMethod]
    public void ApplyColumnOrder_EmptyOrder_LeavesColumnsUnchanged()
    {
        var lst = MakeGridListView("Name", "Path", "DateModified");

        ResultsControlColumns.ApplyColumnOrder(lst, new List<string>());

        CollectionAssert.AreEqual(new[] { "Name", "Path", "DateModified" }, ColumnIds(lst));
    }

    [StaTestMethod]
    public void ApplyColumnOrder_FullOrder_ReordersColumns()
    {
        var lst = MakeGridListView("Name", "Path", "DateModified");

        ResultsControlColumns.ApplyColumnOrder(lst, new List<string> { "DateModified", "Name", "Path" });

        CollectionAssert.AreEqual(new[] { "DateModified", "Name", "Path" }, ColumnIds(lst));
    }

    [StaTestMethod]
    public void ApplyColumnOrder_PartialOrder_UnlistedColumnsKeepRelativeOrderAfterListedOnes()
    {
        var lst = MakeGridListView("Name", "Path", "DateModified", "plugin.custom");

        ResultsControlColumns.ApplyColumnOrder(lst, new List<string> { "DateModified" });

        CollectionAssert.AreEqual(new[] { "DateModified", "Name", "Path", "plugin.custom" }, ColumnIds(lst));
    }
}
