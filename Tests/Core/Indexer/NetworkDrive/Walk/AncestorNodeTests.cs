using Lertaro.Core.Indexer.NetworkDrive.Walk;

namespace Lertaro.Core.Tests.Indexer.NetworkDrive.Walk;

[TestClass]
public sealed class AncestorNodeTests
{
    [TestMethod]
    public void Contains_RootNode_MatchesSelfIgnoringCaseAndSlashes()
    {
        var root = new AncestorNode(@"\\nas\share\folderA\", null);

        Assert.IsTrue(root.Contains(@"\\nas\share\folderA"));
        Assert.IsTrue(root.Contains(@"//NAS/SHARE/FOLDERA/"));
        Assert.IsFalse(root.Contains(@"\\nas\share\folderB"));
    }

    [TestMethod]
    public void Contains_DeepChain_MatchesAnyAncestorInChain()
    {
        var root = new AncestorNode(@"C:\FolderA", null);
        var level1 = new AncestorNode(@"C:\FolderA\SubB", root);
        var level2 = new AncestorNode(@"C:\FolderA\SubB\ChildC", level1);

        Assert.IsTrue(level2.Contains(@"C:\FolderA"));
        Assert.IsTrue(level2.Contains(@"c:\folderA\subB"));
        Assert.IsTrue(level2.Contains(@"C:\FolderA\SubB\ChildC"));
        Assert.IsFalse(level2.Contains(@"C:\FolderA\SubB\OtherD"));
    }

    [TestMethod]
    public void HasSegmentCycle_NoRepeatingSegments_ReturnsFalse()
    {
        var node = new AncestorNode(@"\\nas\share\folderA\subB\childC\deepD", null, isReparsePoint: true);
        Assert.IsFalse(node.HasSegmentCycle());
    }

    [TestMethod]
    public void HasSegmentCycle_ConsecutiveRepeatingSegments_ReturnsTrue()
    {
        var node = new AncestorNode(@"\\nas\share\folderA\symlinkA\symlinkA", null, isReparsePoint: true);
        Assert.IsTrue(node.HasSegmentCycle());
    }

    [TestMethod]
    public void HasSegmentCycle_TwoSegmentCycle_ReturnsTrue()
    {
        var node = new AncestorNode(@"\\nas\share\folderA\subB\linkA\subB\linkA", null, isReparsePoint: true);
        Assert.IsTrue(node.HasSegmentCycle());
    }

    [TestMethod]
    public void HasSegmentCycle_RepeatedNameThatIsAnOrdinaryDirectory_IsNotACycle()
    {
        // The whole point of the reparse-point requirement: "\\nas\data\backup\backup" and "...\src\src"
        // are directories people create, and classifying them as cycles dropped their entire subtree from
        // the index with nothing but an aggregate counter to show for it.
        Assert.IsFalse(new AncestorNode(@"\\nas\share\a\b\b", null).HasSegmentCycle());
        Assert.IsFalse(new AncestorNode(@"\\nas\share\subA\subB\subA\subB", null).HasSegmentCycle());
    }

    [TestMethod]
    public void HasSegmentCycle_OnlyTheTrailingSegmentHasToBeTheLink()
    {
        // The walk descends into links, so the repeat that matters is the one this node arrived through.
        var throughLink = new AncestorNode(@"\\nas\share\data\backup\backup", null, isReparsePoint: true);
        var ordinary = new AncestorNode(@"\\nas\share\data\backup\backup", null, isReparsePoint: false);

        Assert.IsTrue(throughLink.HasSegmentCycle());
        Assert.IsFalse(ordinary.HasSegmentCycle());
    }
}
