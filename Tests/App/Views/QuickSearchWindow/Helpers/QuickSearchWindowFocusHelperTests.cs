using Lertaro.App.Views.QuickSearchWindow.Helpers;

namespace Lertaro.App.Tests.Views.QuickSearchWindow.Helpers;

[TestClass]
public sealed class QuickSearchWindowFocusHelperTests
{
    [TestMethod]
    public void ResolveSearchBoxFocus_ClipboardHandoff_SelectsEverything() =>
        Assert.AreEqual(QuickSearchWindowFocusHelper.SearchBoxFocus.SelectAll,
            QuickSearchWindowFocusHelper.ResolveSearchBoxFocus(selectSearchText: true, caretAtEnd: false));

    [TestMethod]
    public void ResolveSearchBoxFocus_ClipboardHandoffBeatsCarriedOverQuery() =>
        // Select-all exists so the next keystroke replaces the imported text; parking the caret after it
        // instead would leave the two behaviours fighting over the same keystroke.
        Assert.AreEqual(QuickSearchWindowFocusHelper.SearchBoxFocus.SelectAll,
            QuickSearchWindowFocusHelper.ResolveSearchBoxFocus(selectSearchText: true, caretAtEnd: true));

    [TestMethod]
    public void ResolveSearchBoxFocus_CarriedOverQuery_ParksTheCaretAtTheEnd() =>
        Assert.AreEqual(QuickSearchWindowFocusHelper.SearchBoxFocus.CaretAtEnd,
            QuickSearchWindowFocusHelper.ResolveSearchBoxFocus(selectSearchText: false, caretAtEnd: true));

    [TestMethod]
    public void ResolveSearchBoxFocus_PlainSummon_LeavesTheCaretAlone() =>
        Assert.AreEqual(QuickSearchWindowFocusHelper.SearchBoxFocus.LeaveCaret,
            QuickSearchWindowFocusHelper.ResolveSearchBoxFocus(selectSearchText: false, caretAtEnd: false));
}
