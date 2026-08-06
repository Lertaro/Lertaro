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
        var node = new AncestorNode(@"\\nas\share\folderA\subB\childC\deepD", null);
        Assert.IsFalse(node.HasSegmentCycle());
    }

    [TestMethod]
    public void HasSegmentCycle_ConsecutiveRepeatingSegments_ReturnsTrue()
    {
        var node = new AncestorNode(@"\\nas\share\folderA\symlinkA\symlinkA", null);
        Assert.IsTrue(node.HasSegmentCycle());
    }

    [TestMethod]
    public void HasSegmentCycle_TwoSegmentCycle_ReturnsTrue()
    {
        var node = new AncestorNode(@"\\nas\share\folderA\subB\linkA\subB\linkA", null);
        Assert.IsTrue(node.HasSegmentCycle());
    }
}
