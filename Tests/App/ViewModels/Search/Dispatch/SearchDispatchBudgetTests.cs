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
}
