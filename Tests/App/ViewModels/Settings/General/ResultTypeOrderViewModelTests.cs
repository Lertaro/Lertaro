using Lertaro.Core;
using Lertaro.App.ViewModels.Settings.General;

namespace Lertaro.App.Tests.ViewModels.Settings.General;

// The quick window's per-result-type trigger character is a configurable single character matched at the
// START of a query -- exactly where the search syntax does its own reading. It had no validation at all,
// so it could be set to a character the syntax consumes (the trigger silently never fires) or to the same
// character as another type (Save's ToDictionary then threw outright and took Apply down with it).
//
// As with the sibling Order view models, the constructor's own PluginManager enumeration has no seam, so
// these tests seed Items directly.
[TestClass]
public sealed class ResultTypeOrderViewModelTests
{
    private static ResultTypeOrderViewModel MakeViewModel(UserSettings settings, params (string Id, string Trigger)[] items)
    {
        var vm = new ResultTypeOrderViewModel(settings);
        vm.Items.Clear();
        foreach (var (id, trigger) in items)
            vm.Items.Add(new ResultTypeOrderItem(id, () => id, trigger));
        return vm;
    }

    [TestMethod]
    public void Save_TwoTypesSharingATriggerCharacter_DoesNotThrow()
    {
        // Both rows are perfectly legal to type, so Save must survive them; the first claimant keeps the
        // character rather than the whole Apply failing.
        var settings = new UserSettings();
        var vm = MakeViewModel(settings, ("a", "x"), ("b", "x"));

        vm.Save();

        Assert.HasCount(1, settings.ResultTypeTriggers);
        Assert.AreEqual("x", settings.ResultTypeTriggers["a"]);
    }

    [TestMethod]
    public void Save_DistinctTriggers_AreAllPersisted()
    {
        var settings = new UserSettings();
        var vm = MakeViewModel(settings, ("a", "x"), ("b", "y"));

        vm.Save();

        Assert.HasCount(2, settings.ResultTypeTriggers);
        Assert.AreEqual("x", settings.ResultTypeTriggers["a"]);
        Assert.AreEqual("y", settings.ResultTypeTriggers["b"]);
    }

    [TestMethod]
    public void Save_EmptyTrigger_IsNotPersisted()
    {
        var settings = new UserSettings();
        var vm = MakeViewModel(settings, ("a", string.Empty), ("b", "y"));

        vm.Save();

        Assert.HasCount(1, settings.ResultTypeTriggers);
        Assert.AreEqual("y", settings.ResultTypeTriggers["b"]);
    }

    [TestMethod]
    public void Item_Error_IsSilentUntilSet()
    {
        // The row renders its message only when one is present; an unset item must not show an empty
        // banner.
        var item = new ResultTypeOrderItem("a", () => "a", "x");

        Assert.IsNull(item.Error);
        Assert.IsFalse(item.HasError);
    }

    [TestMethod]
    public void Item_Error_RaisesHasErrorNotification()
    {
        var item = new ResultTypeOrderItem("a", () => "a", "x");
        var changed = new List<string>();
        item.PropertyChanged += (_, e) => changed.Add(e.PropertyName ?? string.Empty);

        item.Error = "nope";

        Assert.IsTrue(item.HasError);
        Assert.Contains(nameof(ResultTypeOrderItem.HasError), changed);
    }

    [TestMethod]
    public void Item_TriggerChar_Change_InvokesTheCallback()
    {
        // The owning view model re-validates every row on any change, because a duplicate is a property of
        // the set rather than of the row that was typed in.
        var calls = 0;
        var item = new ResultTypeOrderItem("a", () => "a", string.Empty, () => calls++);

        item.TriggerChar = "x";

        Assert.AreEqual(1, calls);
    }
}
