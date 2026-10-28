using Lertaro.App.ViewModels.Search.Dispatch;

namespace Lertaro.App.Tests.ViewModels.Search.Dispatch;

[TestClass]
public sealed class SearchDispatchBudgetTests
{
    [TestMethod]
    [DataRow("plan.md", true)]
    [DataRow("report.final.docx", true)]
    [DataRow("fast stone", false)]
    [DataRow(".md", false)] // a leading dot reads as an extension fragment, not a name -- never an operator
    [DataRow("-plan.md", false)] // ditto a leading dash
    [DataRow("plan", false)]
    // '!' '^' '\'' are ordinary name characters now: the search box stopped reading them as operators, so a
    // query that opens with one really does name a file and deserves the wider budget.
    [DataRow("!plan.md", true)]
    [DataRow("^plan.md", true)]
    [DataRow("'plan.md", true)]
    // The live triggers are all syntax, so none of them is a name.
    [DataRow("?plan.md", false)] // precision inversion
    [DataRow(":plan.md", false)] // exclusion
    [DataRow("/plan.md", false)] // regex clause / path
    [DataRow(@"\plan.md", false)] // plugin token (the shipped prefix)
    [DataRow("<plan.md", false)] // sort token
    public void LooksLikeFileNameQuery_UsesTheExtensionAsTheSignal(string query, bool expected) =>
        Assert.AreEqual(expected, SearchDispatchController.LooksLikeFileNameQuery(query));

    [TestMethod]
    public void LooksLikeFileNameQuery_ReadsTheConfiguredPrefixRatherThanACopy()
    {
        // The same word is a name under one prefix and a token under another -- one authority decides.
        Assert.IsTrue(SearchDispatchController.LooksLikeFileNameQuery("!plan.md", '\\'));
        Assert.IsFalse(SearchDispatchController.LooksLikeFileNameQuery("!plan.md", '!'));
    }

    [TestMethod]
    [DataRow(0, false, true)] // an empty snapshot before the last one means rows have not arrived yet
    [DataRow(0, true, false)] // the final one is a real answer, and carries its own "no results" row
    [DataRow(1, false, false)]
    [DataRow(9, true, false)]
    public void HoldsPreviousRender_OnlyStandsDownForAnEmptyIntermediate(int rowCount, bool final, bool expected) =>
        Assert.AreEqual(expected, SearchDispatchController.HoldsPreviousRender(rowCount, final));
}
