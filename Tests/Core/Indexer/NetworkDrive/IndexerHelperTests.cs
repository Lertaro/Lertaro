using Lertaro.Core.Indexer.NetworkDrive;

namespace Lertaro.Core.Tests.Indexer.NetworkDrive;

[TestClass]
public sealed class IndexerHelperTests
{
    [TestMethod]
    public void ComputeExclusionFingerprint_SameMembersDifferentOrder_ProducesSameFingerprint()
    {
        var a = IndexerHelper.ComputeExclusionFingerprint(
            new[] { @"C:\a", @"C:\b" }, new[] { "*.tmp" }, new[] { "^foo$" });
        var b = IndexerHelper.ComputeExclusionFingerprint(
            new[] { @"C:\b", @"C:\a" }, new[] { "*.tmp" }, new[] { "^foo$" });

        Assert.AreEqual(a, b);
    }

    [TestMethod]
    public void ComputeExclusionFingerprint_CaseAndWhitespaceDifferences_AreNormalizedAway()
    {
        var a = IndexerHelper.ComputeExclusionFingerprint(new[] { @"C:\a" }, [], []);
        var b = IndexerHelper.ComputeExclusionFingerprint(new[] { "  c:\\a  " }, [], []);

        Assert.AreEqual(a, b);
    }

    [TestMethod]
    public void ComputeExclusionFingerprint_SameLiteralInDifferentCategories_ProducesDifferentFingerprint()
    {
        var pathOnly = IndexerHelper.ComputeExclusionFingerprint(new[] { "foo" }, [], []);
        var globOnly = IndexerHelper.ComputeExclusionFingerprint([], new[] { "foo" }, []);

        Assert.AreNotEqual(pathOnly, globOnly);
    }

    [TestMethod]
    public void ComputeExclusionFingerprint_DifferentMembers_ProducesDifferentFingerprint()
    {
        var a = IndexerHelper.ComputeExclusionFingerprint(new[] { @"C:\a" }, [], []);
        var b = IndexerHelper.ComputeExclusionFingerprint(new[] { @"C:\b" }, [], []);

        Assert.AreNotEqual(a, b);
    }

    [TestMethod]
    public void ComputeExclusionFingerprint_BlankAndEmptyEntries_AreIgnored()
    {
        var a = IndexerHelper.ComputeExclusionFingerprint(new[] { @"C:\a" }, [], []);
        var b = IndexerHelper.ComputeExclusionFingerprint(new[] { @"C:\a", "", "   " }, [], []);

        Assert.AreEqual(a, b);
    }

    [TestMethod]
    public void NormalizeFilter_Null_ReturnsNull() => Assert.IsNull(IndexerHelper.NormalizeFilter(null));

    [TestMethod]
    public void NormalizeFilter_Whitespace_ReturnsNull() => Assert.IsNull(IndexerHelper.NormalizeFilter("   "));

    [TestMethod]
    public void NormalizeFilter_ForwardSlashesAndCasing_NormalizedWithTrailingSeparator()
    {
        var result = IndexerHelper.NormalizeFilter("Sub/Folder");

        Assert.AreEqual(@"sub\folder\", result);
    }

    [TestMethod]
    public void NormalizeFilter_AlreadyHasTrailingSeparator_NotDoubled()
    {
        var result = IndexerHelper.NormalizeFilter(@"sub\folder\");

        Assert.AreEqual(@"sub\folder\", result);
    }

    [TestMethod]
    public void NormalizeDrive_Empty_ReturnsEmpty() => Assert.AreEqual(string.Empty, IndexerHelper.NormalizeDrive(""));

    [TestMethod]
    public void NormalizeDrive_UncPath_NormalizedAndTrailingSeparatorTrimmed() => Assert.AreEqual(@"\\server\share", IndexerHelper.NormalizeDrive("//server/share/"));

    [TestMethod]
    [DataRow("D", "D")]
    [DataRow("d:", "D")]
    [DataRow(@"d:\", "D")]
    public void NormalizeDrive_BareDriveLetterVariants_CollapseToUppercaseLetter(string input, string expected) => Assert.AreEqual(expected, IndexerHelper.NormalizeDrive(input));

    [TestMethod]
    public void NormalizeDrive_FolderIndexTarget_KeepsFullPathNormalized() => Assert.AreEqual(@"D:\Projects", IndexerHelper.NormalizeDrive("D:/Projects/"));

    [TestMethod]
    [DataRow("15Minutes", "15Minutes")]
    [DataRow("Hourly", "Hourly")]
    [DataRow("Daily", "Daily")]
    [DataRow("Bogus", "Manual")]
    [DataRow(null, "Manual")]
    public void NormalizeRefreshMode_ReturnsKnownModeOrManual(string? input, string expected) => Assert.AreEqual(expected, IndexerHelper.NormalizeRefreshMode(input));

    [TestMethod]
    public void GetRefreshInterval_KnownModes_ReturnExpectedTimeSpans()
    {
        Assert.AreEqual(TimeSpan.FromMinutes(15), IndexerHelper.GetRefreshInterval("15Minutes"));
        Assert.AreEqual(TimeSpan.FromHours(1), IndexerHelper.GetRefreshInterval("Hourly"));
        Assert.AreEqual(TimeSpan.FromDays(1), IndexerHelper.GetRefreshInterval("Daily"));
    }

    [TestMethod]
    public void GetRefreshInterval_Manual_ReturnsNull() => Assert.IsNull(IndexerHelper.GetRefreshInterval("Manual"));
}
