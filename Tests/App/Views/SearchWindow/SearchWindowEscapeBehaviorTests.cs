using Lertaro.App.Views.SearchWindow;

namespace Lertaro.App.Tests.Views.SearchWindow;

// The gate behind the "keep search box content" setting on the FULL search window's Escape key. Only the
// decision is covered: acting on it needs a live window, which is why it was pulled out of the handler.
[TestClass]
public sealed class SearchWindowEscapeBehaviorTests
{
    [TestMethod]
    public void AnEmptyBoxClosesOnTheFirstEscape() =>
        // Unchanged behaviour, with or without the setting: nothing to empty, so it was always the close.
        Assert.IsTrue(SearchWindowInputHandler.ShouldCloseOnEscape(boxText: "", keepSearchText: false));

    [TestMethod]
    public void ATypedBoxIsEmptiedFirstWhenNothingIsKept() =>
        Assert.IsFalse(SearchWindowInputHandler.ShouldCloseOnEscape(boxText: "report", keepSearchText: false));

    [TestMethod]
    public void AKeptBoxClosesInsteadOfBeingEmptied() =>
        // The point of the setting: Escape must not destroy what the user asked to keep.
        Assert.IsTrue(SearchWindowInputHandler.ShouldCloseOnEscape(boxText: "report", keepSearchText: true));

    [TestMethod]
    public void ANullBoxStillCloses() =>
        // The box control's Text is never null in practice, but the gate does not lean on that.
        Assert.IsTrue(SearchWindowInputHandler.ShouldCloseOnEscape(boxText: null, keepSearchText: false));
}
