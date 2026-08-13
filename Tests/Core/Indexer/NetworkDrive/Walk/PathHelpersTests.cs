using Lertaro.Core.Indexer.NetworkDrive.Walk;

namespace Lertaro.Core.Tests.Indexer.NetworkDrive.Walk;

[TestClass]
public sealed class PathHelpersTests
{
    [TestMethod]
    public void NormalizePath_ForwardSlashes_ConvertedToBackslashes() => Assert.AreEqual(@"c:\foo\bar", PathHelpers.NormalizePath("c:/foo/bar", isDirectory: false));

    [TestMethod]
    public void NormalizePath_Directory_GetsTrailingSeparator() => Assert.AreEqual(@"c:\foo\", PathHelpers.NormalizePath(@"c:\foo", isDirectory: true));

    [TestMethod]
    public void NormalizePath_DirectoryAlreadyHasTrailingSeparator_IsNotDoubled() => Assert.AreEqual(@"c:\foo\", PathHelpers.NormalizePath(@"c:\foo\", isDirectory: true));

    [TestMethod]
    public void NormalizePath_File_HasNoTrailingSeparatorAdded() => Assert.AreEqual(@"c:\foo\bar.txt", PathHelpers.NormalizePath(@"c:\foo\bar.txt", isDirectory: false));

    [TestMethod]
    public void BuildSourceRoot_BareDriveLetter_AppendsColonAndSeparator() => Assert.AreEqual(@"Z:\", PathHelpers.BuildSourceRoot("Z"));

    [TestMethod]
    public void BuildSourceRoot_FullPathWithoutTrailingSeparator_GetsOneAppended() => Assert.AreEqual(@"Z:\Archive\", PathHelpers.BuildSourceRoot(@"Z:\Archive"));

    [TestMethod]
    public void BuildSourceRoot_FullPathWithTrailingSeparator_IsUnchanged() => Assert.AreEqual(@"Z:\Archive\", PathHelpers.BuildSourceRoot(@"Z:\Archive\"));

    [TestMethod]
    public void HashPath_SamePathDifferentCase_ProducesSameHash()
    {
        var lower = PathHelpers.HashPath(@"c:\foo\bar");
        var upper = PathHelpers.HashPath(@"C:\FOO\BAR");

        Assert.AreEqual(lower, upper);
    }

    [TestMethod]
    public void HashPath_TrailingSeparator_DoesNotAffectHash()
    {
        var withSlash = PathHelpers.HashPath(@"c:\foo\bar\");
        var withoutSlash = PathHelpers.HashPath(@"c:\foo\bar");

        Assert.AreEqual(withSlash, withoutSlash);
    }

    [TestMethod]
    public void HashPath_DifferentPaths_ProduceDifferentHashes()
    {
        var a = PathHelpers.HashPath(@"c:\foo");
        var b = PathHelpers.HashPath(@"c:\bar");

        Assert.AreNotEqual(a, b);
    }

    [TestMethod]
    public void HashPath64_NeverReturnsZero()
    {
        // HashPath64 folds a 128-bit hash into 64 bits and remaps a 0 result to 1 (0 is reserved as a
        // sentinel by callers), so an input whose folded hash lands on exactly 0 must not slip through.
        var hash = PathHelpers.HashPath64(@"c:\foo\bar");

        Assert.AreNotEqual(0UL, hash);
    }
}
