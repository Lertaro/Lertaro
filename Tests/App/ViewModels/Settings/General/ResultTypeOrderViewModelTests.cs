using Lertaro.App.ViewModels.Settings.General;

namespace Lertaro.App.Tests.ViewModels.Settings.General;

// Pins the per-type trigger character's own rules: what is stored, and when the owner is told. One character
// is the whole rule at the runtime end (SearchResultTypePriority.ResolveTrigger only ever reads a length-1
// value), so a longer value is trimmed on the way in rather than saved as a trigger that can never fire.
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
        var item = new ResultTypeOrderItem("files", () => "files", "f", _ => changed++);

        item.TriggerChar = "g";
        item.TriggerChar = "g";
        item.TriggerChar = "  g  ";

        Assert.AreEqual(1, changed, "a no-op write must not send the owner off to recompute every row's warning");
    }

    [TestMethod]
    public void ConflictWarning_RaisesPropertyChangedForItself()
    {
        var item = Item("f");
        var raised = new List<string?>();
        item.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        item.ConflictWarning = "taken";
        item.ConflictWarning = "taken";

        Assert.AreEqual("taken", item.ConflictWarning);
        CollectionAssert.Contains(raised, nameof(ResultTypeOrderItem.ConflictWarning));
        Assert.HasCount(1, raised, "the second write is a no-op");
    }
}

// Pins the rule behind the amber warning under a trigger character: which OTHER row already owns it. Before
// this warned, the settings table happily kept two types on one character and ResolveTrigger then returned
// whichever entry it enumerated first, leaving the other type's trigger silently dead.
[TestClass]
public sealed class ResultTypeOrderViewModelTests
{
    private static ResultTypeOrderItem Item(string id, string triggerChar) => new(id, () => id, triggerChar);

    [TestMethod]
    public void FindDuplicateTrigger_TwoTypesOnOneCharacter_NamesTheOtherEitherWayRound()
    {
        var files = Item("files", "f");
        var apps = Item("apps", "f");
        var items = new[] { files, apps };

        Assert.AreSame(apps, ResultTypeOrderViewModel.FindDuplicateTrigger(items, files));
        Assert.AreSame(files, ResultTypeOrderViewModel.FindDuplicateTrigger(items, apps));
    }

    [TestMethod]
    public void FindDuplicateTrigger_DifferentCase_IsStillADuplicate()
    {
        var upper = Item("files", "F");
        var lower = Item("apps", "f");

        Assert.AreSame(upper, ResultTypeOrderViewModel.FindDuplicateTrigger(new[] { upper, lower }, lower));
    }

    [TestMethod]
    public void FindDuplicateTrigger_UniqueOrUnsetCharacters_ReportNothing()
    {
        var files = Item("files", "f");
        var apps = Item("apps", "a");
        var unset = Item("settings", string.Empty);
        var items = new[] { files, apps, unset };

        Assert.IsNull(ResultTypeOrderViewModel.FindDuplicateTrigger(items, files));
        Assert.IsNull(ResultTypeOrderViewModel.FindDuplicateTrigger(items, unset));
        Assert.IsNull(ResultTypeOrderViewModel.FindDuplicateTrigger(items, apps));
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
