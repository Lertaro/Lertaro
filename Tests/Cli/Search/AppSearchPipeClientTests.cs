using Lertaro.Cli.Search;

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
}
