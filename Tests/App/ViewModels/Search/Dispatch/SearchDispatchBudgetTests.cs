using Lertaro.App.ViewModels.Search.Dispatch;

namespace Lertaro.App.Tests.ViewModels.Search.Dispatch;

[TestClass]
public sealed class SearchDispatchBudgetTests
{
    [TestMethod]
    [DataRow("plan.md", true)]
    [DataRow("report.final.docx", true)]
    [DataRow("fast stone", false)]
    [DataRow(".md", false)]
    [DataRow("!plan.md", false)]
    [DataRow("plan", false)]
    public void LooksLikeFileNameQuery_UsesTheExtensionAsTheSignal(string query, bool expected) =>
        Assert.AreEqual(expected, SearchDispatchController.LooksLikeFileNameQuery(query));

    [TestMethod]
    [DataRow(0, false, true)] // an empty snapshot before the last one means rows have not arrived yet
    [DataRow(0, true, false)] // the final one is a real answer, and carries its own "no results" row
    [DataRow(1, false, false)]
    [DataRow(9, true, false)]
    public void HoldsPreviousRender_OnlyStandsDownForAnEmptyIntermediate(int rowCount, bool final, bool expected) =>
        Assert.AreEqual(expected, SearchDispatchController.HoldsPreviousRender(rowCount, final));
}
