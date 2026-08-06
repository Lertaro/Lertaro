using Lertaro.App.ViewModels.QuickPanel;
using Lertaro.Core;

namespace Lertaro.App.Tests.Views.QuickPanel;

// Whether a drag hovering over a group is one it should take. Three separate questions that must all
// answer yes, and the copy itself is Windows' own (ShellPasteHelper) -- so this is the whole of the
// decision the panel makes, and the part worth pinning without a mouse.
[TestClass]
public sealed class QuickPanelDropTargetTests
{
    private static QuickPanelGroupViewModel Group(string folderPath, bool acceptsDrops)
        => new("s1", "Group", folderPath, new List<(AppSearchResult, DateTime?)>(),
            QuickPanelSortMode.ModifiedDescending, thumbnailView: true, expanded: true, acceptsDrops: acceptsDrops);

    private static bool CanDrop(QuickPanelGroupViewModel? group, bool carriesFiles = true, bool inside = false)
        => Lertaro.App.Views.QuickPanel.QuickPanelWindow.CanDrop(group, carriesFiles, inside);

    // A real directory, because a drop has to land somewhere that exists: TestContext's own folder is
    // one, without this having to make or clean up anything.
    public TestContext TestContext { get; set; } = null!;

    private string ExistingFolder => TestContext.TestRunDirectory ?? System.IO.Path.GetTempPath();

    [TestMethod]
    public void CanDrop_ConfiguredGroupCarryingFiles_IsATarget()
        => Assert.IsTrue(CanDrop(Group(ExistingFolder, acceptsDrops: true)));

    // Off by default and only on when asked: a panel that quietly wrote into whatever folder the pointer
    // was over is a worse thing to get wrong than one that ignores a drop.
    [TestMethod]
    public void CanDrop_SourceNotConfiguredForIt_IsNot()
        => Assert.IsFalse(CanDrop(Group(ExistingFolder, acceptsDrops: false)));

    [TestMethod]
    public void CanDrop_DragCarryingSomethingOtherThanFiles_IsNot()
        => Assert.IsFalse(CanDrop(Group(ExistingFolder, acceptsDrops: true), carriesFiles: false));

    // A row dragged from one group towards another is on its way OUT to some other window. Turning a
    // half-finished drag-out into a real file copy in the folder next door is not a mistake worth being
    // able to make.
    [TestMethod]
    public void CanDrop_DragThatStartedInsideThePanel_IsNot()
        => Assert.IsFalse(CanDrop(Group(ExistingFolder, acceptsDrops: true), inside: true));

    [TestMethod]
    public void CanDrop_FolderThatIsNotThere_IsNot()
        => Assert.IsFalse(CanDrop(Group(@"Z:\definitely-not-a-real-lertaro-folder", acceptsDrops: true)));

    [TestMethod]
    public void CanDrop_SourceWithNoPathAtAll_IsNot()
        => Assert.IsFalse(CanDrop(Group(string.Empty, acceptsDrops: true)));

    [TestMethod]
    public void CanDrop_NoGroupUnderThePointer_IsNot()
        => Assert.IsFalse(CanDrop(null));
}
