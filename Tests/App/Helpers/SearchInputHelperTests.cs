using Lertaro.App.Helpers;

namespace Lertaro.App.Tests.Helpers;

// The gate both search windows share for the "keep search box content" setting. Only the decision is
// covered: selecting needs a live window, so both windows call this and then act on it.
[TestClass]
public sealed class SearchInputHelperTests
{
    [TestMethod]
    public void CarriedTextIsSelectedWhenTheSettingIsOn() =>
        // What the full window does with the query it was handed, and the quick window with what it kept.
        Assert.IsTrue(SearchInputHelper.ShouldSelectCarriedText("report", keepSearchText: true));

    [TestMethod]
    public void NothingIsSelectedWhenTheSettingIsOff() =>
        // Off is the old behaviour: the full window parks the caret after the text so typing appends.
        Assert.IsFalse(SearchInputHelper.ShouldSelectCarriedText("report", keepSearchText: false));

    [TestMethod]
    public void AnEmptyBoxHasNothingToSelect() =>
        // The ordinary "open the full window with no query" case, which must not change.
        Assert.IsFalse(SearchInputHelper.ShouldSelectCarriedText("", keepSearchText: true));

    [TestMethod]
    public void ANullBoxHasNothingToSelect() =>
        Assert.IsFalse(SearchInputHelper.ShouldSelectCarriedText(null, keepSearchText: true));
}
