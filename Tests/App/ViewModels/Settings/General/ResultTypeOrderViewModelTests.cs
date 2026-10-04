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

// Pins what Save writes back. Items only carries the ENABLED providers, so the merge has to tell three
// cases apart: a row the user can see (this dialog owns it, including clearing it), a type id that is still
// loaded but has no row because its provider is switched off (keep it exactly as stored), and an id that is
// neither (an uninstalled plugin's leftovers -- drop it, which is the one thing the old wholesale replace
// got right).
[TestClass]
public sealed class ResultTypeOrderSaveMergeTests
{
    [TestMethod]
    public void MergeTriggers_DisabledProvidersType_KeepsTheTriggerItConfigured()
    {
        // Regression: disabling a plugin's provider and then pressing Apply on ANY settings page used to
        // delete that type's trigger character permanently.
        var stored = new Dictionary<string, string> { ["files"] = "f", ["plugins"] = "p" };

        var merged = ResultTypeOrderViewModel.MergeTriggers(
            new[] { ("files", "f") }, stored, hidden: new[] { "plugins" });

        Assert.AreEqual("p", merged["plugins"], "switched off is not the same as deleted");
        Assert.AreEqual("f", merged["files"]);
    }

    [TestMethod]
    public void MergeTriggers_RowTheUserCleared_IsRemovedRatherThanLeftAtItsStoredValue()
    {
        var stored = new Dictionary<string, string> { ["files"] = "f", ["plugins"] = "p" };

        var merged = ResultTypeOrderViewModel.MergeTriggers(
            new[] { ("files", string.Empty) }, stored, hidden: new[] { "plugins" });

        Assert.IsFalse(merged.ContainsKey("files"), "an empty TriggerChar means the user took the trigger away");
        Assert.AreEqual("p", merged["plugins"]);
    }

    [TestMethod]
    public void MergeTriggers_UninstalledPluginLeftover_IsDropped()
    {
        var stored = new Dictionary<string, string> { ["files"] = "f", ["uninstalled-long-ago"] = "z" };

        var merged = ResultTypeOrderViewModel.MergeTriggers(
            new[] { ("files", "f") }, stored, hidden: Array.Empty<string>());

        CollectionAssert.AreEqual(new[] { "files" }, merged.Keys);
    }

    [TestMethod]
    public void MergeOrder_VisibleRowsComeFirst_AndHiddenOnesKeepTheirRelativeOrder()
    {
        var stored = new List<string> { "plugins", "files", "apps" };

        var merged = ResultTypeOrderViewModel.MergeOrder(
            new[] { "apps", "files" }, stored, hidden: new[] { "plugins" });

        CollectionAssert.AreEqual(new[] { "apps", "files", "plugins" }, merged);
    }

    [TestMethod]
    public void MergeOrder_DoesNotListAnIdTwice()
    {
        // "files" is visible AND present in the stored order; the hidden filter is what keeps the merge from
        // appending a second copy behind the row that already carries it.
        var stored = new List<string> { "files", "plugins" };

        var merged = ResultTypeOrderViewModel.MergeOrder(new[] { "files" }, stored, hidden: new[] { "plugins" });

        CollectionAssert.AreEqual(new[] { "files", "plugins" }, merged);
        Assert.AreEqual(2, merged.Distinct().Count());
    }
}
