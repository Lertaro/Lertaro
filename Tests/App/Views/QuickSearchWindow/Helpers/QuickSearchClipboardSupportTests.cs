using Lertaro.App.Views.QuickSearchWindow.Helpers;

namespace Lertaro.App.Tests.Views.QuickSearchWindow.Helpers;

[TestClass]
public sealed class QuickSearchClipboardSupportTests
{
    [TestMethod]
    public void ShouldApply_NewText_ReturnsTrue() => Assert.IsTrue(
        QuickSearchClipboardSupport.ShouldApply("new query", "old query"));

    [TestMethod]
    public void ShouldApply_SameText_ReturnsFalse() => Assert.IsFalse(
        QuickSearchClipboardSupport.ShouldApply("same query", "same query"));

    [TestMethod]
    public void ShouldApply_EmptyOrWhitespaceText_ReturnsFalse()
    {
        Assert.IsFalse(QuickSearchClipboardSupport.ShouldApply(string.Empty, "old query"));
        Assert.IsFalse(QuickSearchClipboardSupport.ShouldApply("   ", "old query"));
    }

    [TestMethod]
    public void ShouldApply_NullText_ReturnsFalse() => Assert.IsFalse(
        QuickSearchClipboardSupport.ShouldApply(null, "old query"));

    [TestMethod]
    public void ShouldReadClipboard_OnlyWhenNoExplicitQueryExists()
    {
        Assert.IsTrue(QuickSearchClipboardSupport.ShouldReadClipboard(null));
        Assert.IsFalse(QuickSearchClipboardSupport.ShouldReadClipboard(string.Empty));
        Assert.IsFalse(QuickSearchClipboardSupport.ShouldReadClipboard("restored query"));
    }
}
