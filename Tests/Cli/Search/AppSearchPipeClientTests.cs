using Lertaro.Cli.Search;
using Lertaro.Cli.Space;
using Lertaro.Core.IndexV2.Space;

namespace Lertaro.Cli.Tests.Search;

[TestClass]
public sealed class AppSearchPipeClientTests
{
    [TestMethod]
    public void PipeNameFor_UsesTheSessionHash() => Assert.AreEqual(
        "Lertaro_App_Search_Pipe_session-hash",
        AppSearchPipeClient.PipeNameFor("session-hash"));

    [TestMethod]
    [DataRow(8, false)]
    [DataRow(9, true)]
    [DataRow(10, false)]
    [DataRow(49, false)]
    [DataRow(50, true)]
    [DataRow(51, false)]
    public void ProgressSnapshotsAreSentAtTheInitialAndFullVisibleCounts(int streamedCount, bool expected) =>
        Assert.AreEqual(expected, AppSearchPipeClient.ShouldRenderProgressSnapshot(streamedCount));

    [TestMethod]
    public void SpaceEntriesCommand_ParsesDirectory()
    {
        var parsed = SpaceEntriesCommand.TryGetDirectory([SpaceEntriesCommand.Switch, @"C:\Data"], out var directory);

        Assert.IsTrue(parsed);
        Assert.AreEqual(@"C:\Data", directory);
    }

    [TestMethod]
    public void SpaceEntriesCommand_ParsesEmptyDirectoryAsRoots()
    {
        var parsed = SpaceEntriesCommand.TryGetDirectory([SpaceEntriesCommand.Switch, string.Empty], out var directory);

        Assert.IsTrue(parsed);
        Assert.AreEqual(string.Empty, directory);
    }

    [TestMethod]
    public void SpaceEntriesCommand_RejectsMalformedInvocation() =>
        Assert.IsFalse(SpaceEntriesCommand.TryGetDirectory([SpaceEntriesCommand.Switch], out _));

    [TestMethod]
    public void SpaceEntriesJsonFormatter_WritesSizesAsInvariantStrings()
    {
        var json = SpaceEntriesJsonFormatter.Serialize([new SpaceIndexEntry(@"C:\Data", "Data", 10_000_000_000_000, true, false)]);

        StringAssert.Contains(json, "\"Size\":\"10000000000000\"");
    }
}
