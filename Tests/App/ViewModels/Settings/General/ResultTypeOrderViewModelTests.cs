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
