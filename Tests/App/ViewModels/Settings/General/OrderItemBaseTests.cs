using Lertaro.App.ViewModels.Settings.General;

namespace Lertaro.App.Tests.ViewModels.Settings.General;

[TestClass]
public sealed class OrderItemBaseTests
{
    // ColumnOrderItem is a bare OrderItemBase subclass (no extra fields), so it stands in for the whole
    // family (QuickNavProviderOrderItem/ActionMenuGroupOrderItem/SidebarGroupOrderItem share the exact
    // same shape) -- what's under test here is OrderItemBase's own mechanism, not anything column-specific.

    [TestMethod]
    public void DisplayName_ReadsResolverEveryTime_NotJustOnce()
    {
        var current = "first";
        var item = new ColumnOrderItem("id", () => current);

        Assert.AreEqual("first", item.DisplayName);
        current = "second";
        Assert.AreEqual("second", item.DisplayName);
    }

    [TestMethod]
    public void NotifyLanguageChanged_RaisesPropertyChangedForDisplayName()
    {
        var item = new ColumnOrderItem("id", () => "name");
        var raised = new List<string?>();
        item.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        item.NotifyLanguageChanged();

        CollectionAssert.Contains(raised, nameof(ColumnOrderItem.DisplayName));
    }

    [TestMethod]
    public void Id_IsSetFromConstructorAndNeverChanges()
    {
        var item = new ColumnOrderItem("stable-id", () => "name");

        Assert.AreEqual("stable-id", item.Id);
    }
}
