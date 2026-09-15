using Lertaro.Core;
using Lertaro.App.ViewModels.Settings.General;

namespace Lertaro.App.Tests.ViewModels.Settings.General;

// Pins the per-type trigger character's own rules: what a row stores, and when the owner is told. One
// character is the whole rule at the runtime end (SearchResultTypePriority.ResolveTrigger only ever reads a
// length-1 value, ignoring case), so a longer value is trimmed on the way in rather than saved as a trigger
// that can never fire.
[TestClass]
public sealed class ResultTypeOrderItemTests
{
    private static ResultTypeOrderItem Item(string triggerChar) => new("files", () => "files", triggerChar);

    [TestMethod]
    public void TriggerChar_KeepsOnlyTheFirstCharacter()
    {
        Assert.AreEqual("f", Item("  foo ").TriggerChar);
        Assert.AreEqual(string.Empty, Item("   ").TriggerChar);
        Assert.AreEqual("，", Item("，").TriggerChar);
    }

    [TestMethod]
    public void TriggerChar_ChangeNotifiesItsOwnerOncePerRealEdit()
    {
        var changed = 0;
        var item = new ResultTypeOrderItem("files", () => "files", "f", () => changed++);

        item.TriggerChar = "g";
        item.TriggerChar = "g";
        item.TriggerChar = "  g  ";

        Assert.AreEqual(1, changed, "a no-op write must not send the owner off to revalidate every row");
    }
}

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

    // The two characters the runtime cannot tell apart are the same collision here: ResolveTrigger compares
    // the typed first character ignoring case, so "x" and "X" are one trigger with one winner.
    [TestMethod]
    public void Save_TriggersDifferingOnlyByCase_SurviveAsOneClaimant()
    {
        var settings = new UserSettings();
        var vm = MakeViewModel(settings, ("a", "x"), ("b", "X"));

        vm.Save();

        Assert.HasCount(1, settings.ResultTypeTriggers);
        Assert.AreEqual("x", settings.ResultTypeTriggers["a"]);
    }

    [TestMethod]
    public void ValidateTriggers_TwoTypesOnOneCharacter_FlagEveryClaimant()
    {
        // A duplicate is a property of the set, not of the row that was typed in last: both rows are the
        // one that has to change, so both have to say so.
        var vm = MakeViewModel(new UserSettings(), ("a", "x"), ("b", "x"));

        vm.ValidateTriggers();

        Assert.IsNotNull(vm.Items[0].Error);
        Assert.IsNotNull(vm.Items[1].Error);
        Assert.IsTrue(vm.Items[0].HasError);
    }

    [TestMethod]
    public void ValidateTriggers_CharactersDifferingOnlyByCase_AreTheSameDuplicate()
    {
        var vm = MakeViewModel(new UserSettings(), ("a", "x"), ("b", "X"));

        vm.ValidateTriggers();

        Assert.IsNotNull(vm.Items[0].Error);
        Assert.IsNotNull(vm.Items[1].Error);
    }

    [TestMethod]
    public void ValidateTriggers_ReservedLeadingCharacter_IsFlagged()
    {
        // '*' is the one-search exclusion bypass: the search box consumes it before any per-type trigger is
        // read, so such a trigger can never fire (see SearchSyntaxReserved).
        var vm = MakeViewModel(new UserSettings(), ("a", "*"));

        vm.ValidateTriggers();

        Assert.IsNotNull(vm.Items[0].Error);
    }

    [TestMethod]
    public void ValidateTriggers_UniqueUsableCharacters_AndClearedOnes_StaySilent()
    {
        var vm = MakeViewModel(new UserSettings(), ("a", "x"), ("b", "y"), ("c", string.Empty));

        vm.ValidateTriggers();
        Assert.IsNull(vm.Items[0].Error);
        Assert.IsNull(vm.Items[1].Error);
        Assert.IsNull(vm.Items[2].Error);

        // Backing a colliding character out has to take its message with it, or the row stays flagged for a
        // problem the user just removed.
        vm.Items[0].TriggerChar = "y";
        vm.ValidateTriggers();
        Assert.IsNotNull(vm.Items[0].Error);
        Assert.IsNotNull(vm.Items[1].Error);

        vm.Items[0].TriggerChar = string.Empty;
        vm.ValidateTriggers();
        Assert.IsNull(vm.Items[0].Error);
        Assert.IsNull(vm.Items[1].Error);
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
