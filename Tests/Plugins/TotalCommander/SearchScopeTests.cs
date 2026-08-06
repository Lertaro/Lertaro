namespace Lertaro.Plugins.TotalCommander.Tests;

/// <summary>
/// Covers when the adapter may ask Total Commander for its source panel path. That question is a
/// synchronous WM_COPYDATA which makes TC abandon an in-progress quick rename, so when it is skipped
/// matters as much as what it answers. The window lookups are substituted, so these run without TC.
/// </summary>
[TestClass]
public class SearchScopeTests
{
    private static readonly IntPtr Tc = new(0x1234);

    private sealed class Query
    {
        public int Calls;
        public string? Result = @"D:\work";

        public Func<IntPtr, string?> Fn => _ => { Calls++; return Result; };
    }

    [TestMethod]
    public void ReturnsTheQueriedPath()
    {
        var (adapter, query) = (new TotalCommanderInlineSearchAdapter(), new Query());

        Assert.AreEqual(@"D:\work", adapter.GetSearchScopeCore(Tc, _ => false, query.Fn));
        Assert.AreEqual(1, query.Calls);
    }

    [TestMethod]
    [DataRow(@"D:\work\", @"D:\work")]
    [DataRow(@"D:\", @"D:\")]        // a drive root keeps its separator
    [DataRow(@"D:\work", @"D:\work")]
    public void TrimsATrailingSeparatorExceptOnADriveRoot(string queried, string expected)
    {
        var adapter = new TotalCommanderInlineSearchAdapter();
        Assert.AreEqual(expected, adapter.GetSearchScopeCore(Tc, _ => false, _ => queried));
    }

    [TestMethod]
    public void ReturnsNullWhenTotalCommanderDoesNotAnswer()
    {
        Assert.IsNull(new TotalCommanderInlineSearchAdapter().GetSearchScopeCore(Tc, _ => false, _ => null));
        Assert.IsNull(new TotalCommanderInlineSearchAdapter().GetSearchScopeCore(Tc, _ => false, _ => string.Empty));
    }

    [TestMethod]
    public void DoesNotAskWhileAQuickRenameIsOpen()
    {
        // Issue #189: servicing the question made Total Commander drop the rename editor, so renaming was
        // impossible for as long as Lertaro was running.
        var (adapter, query) = (new TotalCommanderInlineSearchAdapter(), new Query());

        adapter.GetSearchScopeCore(Tc, _ => true, query.Fn);

        Assert.AreEqual(0, query.Calls);
    }

    [TestMethod]
    public void KeepsTheLastKnownPathWhileAQuickRenameIsOpen()
    {
        // The panel cannot change directory while its own rename is up, so the previous answer still holds.
        // Collapsing the scope to null instead would wipe the tracked path mid-rename.
        var (adapter, query) = (new TotalCommanderInlineSearchAdapter(), new Query());

        adapter.GetSearchScopeCore(Tc, _ => false, query.Fn);

        Assert.AreEqual(@"D:\work", adapter.GetSearchScopeCore(Tc, _ => true, query.Fn));
        Assert.AreEqual(1, query.Calls);
    }

    [TestMethod]
    public void AsksAgainOnceTheRenameCloses()
    {
        var (adapter, query) = (new TotalCommanderInlineSearchAdapter(), new Query());
        adapter.GetSearchScopeCore(Tc, _ => true, query.Fn);

        query.Result = @"D:\elsewhere";

        Assert.AreEqual(@"D:\elsewhere", adapter.GetSearchScopeCore(Tc, _ => false, query.Fn));
    }

    [TestMethod]
    public void FocusInsideAnEditControlIsEnoughOnItsOwn()
    {
        // Total Commander does move focus into the editor -- the hook log reported the focused control as
        // Edit for as long as the box was up -- so the pane never has to be walked in that case.
        var walked = false;
        Assert.IsTrue(TotalCommanderInlineSearchAdapter.IsQuickRenameOpenCore("Edit", () => { walked = true; return false; }));
        Assert.IsFalse(walked);
    }

    [TestMethod]
    public void ThePaneIsWalkedWhileFocusHasNotReachedTheEditorYet()
    {
        // Focus lands there ~36ms after F2. An event arriving inside that gap still reports the pane as
        // focused, and without this it would sail past the check and cancel the rename anyway.
        Assert.IsTrue(TotalCommanderInlineSearchAdapter.IsQuickRenameOpenCore("LCLListBox1", () => true));
        Assert.IsFalse(TotalCommanderInlineSearchAdapter.IsQuickRenameOpenCore("LCLListBox1", () => false));
        Assert.IsTrue(TotalCommanderInlineSearchAdapter.IsQuickRenameOpenCore("TMyListBox2", () => true));
    }

    [TestMethod]
    public void NothingButAPaneIsWalked()
    {
        // The walk enumerates another process's windows, and every unrelated focus change reaches this too.
        var walked = false;
        Assert.IsFalse(TotalCommanderInlineSearchAdapter.IsQuickRenameOpenCore("TMyPanel", () => { walked = true; return true; }));
        Assert.IsFalse(walked);
    }

    [TestMethod]
    [DataRow("Edit", true)]
    [DataRow("TEdit", true)]      // plausible Delphi naming
    [DataRow("TMyEdit", true)]
    [DataRow("EDIT", true)]
    [DataRow("Button", false)]    // appears alongside the editor during a rename
    [DataRow("LCLListBox1", false)]
    [DataRow("", false)]
    public void RecognisesTheEditorByClassName(string className, bool expected)
    {
        Assert.AreEqual(expected, TotalCommanderInlineSearchAdapter.IsEditorClass(className));
    }
}
