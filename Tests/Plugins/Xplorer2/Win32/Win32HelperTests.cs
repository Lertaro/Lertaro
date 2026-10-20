using Lertaro.Plugins.Xplorer2.Win32;

namespace Lertaro.Plugins.Xplorer2.Tests.Win32;

/// <summary>
/// Covers the two decisions the xplorer2 adapter makes on data it reads out of a live window: which
/// address-bar text is actually the pane's folder, and which control should receive a typed path.
/// Values are the shapes measured on a real install (two windows, several tabs each), reduced to generic
/// drive letters.
/// </summary>
[TestClass]
public class Win32HelperTests
{
    [TestMethod]
    [DataRow("D:\\Tools\\App\\AppInfo\\Launcher")]
    [DataRow("C:\\Windows")]
    [DataRow("\\\\server\\share\\folder")]
    public void ChoosePath_AcceptsARealFolderPath(string path)
    {
        Assert.AreEqual(path, Win32Helper.ChoosePath(new[] { path }));
    }

    [TestMethod]
    public void ChoosePath_KeepsDriveRootSeparatorButTrimsItElsewhere()
    {
        // A drive root has no other spelling: trimming "D:\" would produce "D:", which is not the same path.
        Assert.AreEqual("D:\\", Win32Helper.ChoosePath(new[] { "D:\\" }));
        Assert.AreEqual("D:\\Downloads", Win32Helper.ChoosePath(new[] { "D:\\Downloads\\" }));
        Assert.AreEqual("C:\\Windows", Win32Helper.ChoosePath(new[] { "  C:\\Windows  " }));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("265120")]                       // the transient value the same combo reports mid-edit
    [DataRow("::{20D04FE0-3AEA-1069-A2D8-08002B30309D}")]   // shell namespace root kept in its history
    [DataRow("Launcher @ D:\\")]              // the elided form the window title shows instead
    [DataRow("relative\\folder")]
    public void ChoosePath_RejectsNonFolderPath(string text)
    {
        Assert.IsNull(Win32Helper.ChoosePath(new[] { text }));
    }

    [TestMethod]
    public void ChoosePath_TakesTheFirstUsableTextAndIgnoresEmptyControls()
    {
        // With crumbs displayed the classic address bar combo is empty and the crumb combo carries the
        // path; with DPF_NOCRUMBS it is the other way round. Order-insensitive reading is the point.
        var texts = new[] { "", "D:\\Tools\\App\\AppInfo\\Launcher", "C:\\Windows" };
        Assert.AreEqual("D:\\Tools\\App\\AppInfo\\Launcher", Win32Helper.ChoosePath(texts));
    }

    [TestMethod]
    public void ChoosePath_ReturnsNullWhenNothingQualifies()
    {
        Assert.IsNull(Win32Helper.ChoosePath(new[] { "", "12345" }));
        Assert.IsNull(Win32Helper.ChoosePath(Array.Empty<string>()));
    }

    [TestMethod]
    public void ChoosePath_SurvivesNullEntries()
    {
        Assert.AreEqual("C:\\Windows", Win32Helper.ChoosePath(new[] { null!, "C:\\Windows" }));
    }

    [TestMethod]
    [DataRow("SysListView32", true, true)]      // a folder pane, inside its Miller container
    [DataRow("SysListView32", false, false)]    // the mini-scrap bin: same class, no Miller container
    [DataRow("Edit", true, false)]              // the filter box / address bar editor
    [DataRow("ATL:FolderTreeView", true, false)]
    [DataRow("", true, false)]
    public void IsFileListCore_RequiresBothTheClassAndThePaneContainer(string className, bool insideMiller, bool expected)
    {
        var focused = new IntPtr(0x1234);
        var actual = Win32Helper.IsFileListCore(focused, className, _ => insideMiller);
        Assert.AreEqual(expected, actual);
    }

    [TestMethod]
    public void IsFileListCore_NeverAsksTheContainerForAnEmptyHandle()
    {
        var asked = false;
        var actual = Win32Helper.IsFileListCore(IntPtr.Zero, "SysListView32", _ => { asked = true; return true; });
        Assert.IsFalse(actual);
        Assert.IsFalse(asked);
    }

    [TestMethod]
    public void PickPathCombo_IgnoresTheHiddenComboHoldingAStalePath()
    {
        // The measured shape of the bug: crumbs displayed, so the classic address bar combo is hidden but
        // still reports the last path it was given, while the crumb combo reports where the pane actually is.
        var crumb = new IntPtr(0x40C20);
        var classic = new IntPtr(0x80B94);
        var candidates = new[]
        {
            new Win32Helper.ComboCandidate(classic, isVisible: false, parentIsCrumbBar: false, "C:\\Windows"),
            new Win32Helper.ComboCandidate(crumb, isVisible: true, parentIsCrumbBar: true, "D:\\Notes")
        };

        var picked = Win32Helper.PickPathCombo(candidates);
        Assert.IsTrue(picked.HasValue);
        Assert.AreEqual(crumb, picked.Value.HWnd);
    }

    [TestMethod]
    public void PickPathCombo_PrefersTheCrumbBarComboEvenWhenBothAreVisible()
    {
        var crumb = new IntPtr(0x40C20);
        var other = new IntPtr(0x40A6A);
        var candidates = new[]
        {
            // Enumeration order puts the non-crumb combo first, so the crumb bar has to win on identity.
            new Win32Helper.ComboCandidate(other, isVisible: true, parentIsCrumbBar: false, "D:\\first"),
            new Win32Helper.ComboCandidate(crumb, isVisible: true, parentIsCrumbBar: true, "D:\\second")
        };

        Assert.AreEqual(crumb, Win32Helper.PickPathCombo(candidates)!.Value.HWnd);
    }

    [TestMethod]
    public void PickPathCombo_TakesTheOnlyVisibleComboInClassicLayout()
    {
        // DPF_NOCRUMBS: no crumb bar at all, so the plain address bar combo must be accepted on its own.
        var classic = new IntPtr(0x80B94);
        var candidates = new[]
        {
            new Win32Helper.ComboCandidate(classic, isVisible: true, parentIsCrumbBar: false, "D:\\Tools")
        };

        Assert.AreEqual(classic, Win32Helper.PickPathCombo(candidates)!.Value.HWnd);
    }

    [TestMethod]
    [DataRow(false, false, "C:\\Windows")]   // hidden, holding a stale path
    [DataRow(true, false, "")]               // visible but empty
    [DataRow(true, false, "265120")]         // visible mid-edit: transient junk, not a path
    [DataRow(true, false, "::{20D04FE0-3AEA-1069-A2D8-08002B30309D}")]
    public void PickPathCombo_ReturnsNothingWhenNoVisibleComboHoldsAPath(bool visible, bool crumb, string text)
    {
        var candidates = new[] { new Win32Helper.ComboCandidate(new IntPtr(0x1), visible, crumb, text) };
        Assert.IsFalse(Win32Helper.PickPathCombo(candidates).HasValue);
    }

    [TestMethod]
    public void PickPathCombo_SkipsHiddenCombosEvenWhenTheyAreTheOnlyOnesHoldingAPath()
    {
        // Deliberate: a card scoped to the wrong folder is worse than no card at all.
        var candidates = new[]
        {
            new Win32Helper.ComboCandidate(new IntPtr(0x1), isVisible: false, parentIsCrumbBar: true, "D:\\stale")
        };
        Assert.IsFalse(Win32Helper.PickPathCombo(candidates).HasValue);
    }

    [TestMethod]
    public void PickEditor_UsesTheEditorXplorer2FocusedWhenTheCommandReachedAnEdit()
    {
        var focused = new IntPtr(0x40BA0);
        var structured = new IntPtr(0x40CAC);
        Assert.AreEqual(focused, Win32Helper.PickEditor(focused, "Edit", structured));
        // Class comparison is case-insensitive the way the rest of the window matching is.
        Assert.AreEqual(focused, Win32Helper.PickEditor(focused, "edit", structured));
    }

    [TestMethod]
    public void PickEditor_FallsBackToTheStructuredEditorWhenFocusIsElsewhere()
    {
        // Measured: the "Switch to address bar" command sometimes leaves focus on the pane's
        // SysListView32, in which case the address bar edit located by structure must still be written to.
        var listFocus = new IntPtr(0xA0D54);
        var structured = new IntPtr(0x40CAC);
        Assert.AreEqual(structured, Win32Helper.PickEditor(listFocus, "SysListView32", structured));
        Assert.AreEqual(structured, Win32Helper.PickEditor(IntPtr.Zero, "", structured));
    }
}
