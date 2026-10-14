using Lertaro.Core.SearchIndex;

namespace Lertaro.Core.Tests.SearchIndex;

[TestClass]
public sealed class RankSignalsTests
{
    private const uint Now = 1_800_000_000;
    private const uint Hour = 60 * 60;
    private const uint Day = 24 * Hour;

    [TestMethod]
    [DataRow(0u, RankSignals.BandToday)]
    [DataRow(Day - 1, RankSignals.BandToday)]
    [DataRow(Day, RankSignals.BandWeek)]
    [DataRow(7 * Day - 1, RankSignals.BandWeek)]
    [DataRow(7 * Day, RankSignals.BandMonth)]
    [DataRow(30 * Day - 1, RankSignals.BandMonth)]
    [DataRow(30 * Day, RankSignals.BandOlder)]
    public void RecencyBand_BandEdges(uint age, int expected) =>
        Assert.AreEqual(expected, RankSignals.RecencyBand(Now - age, Now - age, Now));

    // A copied or downloaded file keeps its old modified time but gets a fresh creation time.
    [TestMethod]
    public void RecencyBand_UsesTheLaterOfCreatedAndModified()
    {
        Assert.AreEqual(RankSignals.BandToday, RankSignals.RecencyBand(Now - Hour, Now - 400 * Day, Now));
        Assert.AreEqual(RankSignals.BandToday, RankSignals.RecencyBand(Now - 400 * Day, Now - Hour, Now));
    }

    [TestMethod]
    public void RecencyBand_UnrecordedTimesRankAsOlder() =>
        Assert.AreEqual(RankSignals.BandOlder, RankSignals.RecencyBand(0, 0, Now));

    // Clock drift between machines puts a fresh file slightly in the future; a date years ahead is junk.
    [TestMethod]
    public void RecencyBand_SlightlyFutureCountsAsToday_FarFutureIsNotTrusted()
    {
        Assert.AreEqual(RankSignals.BandToday, RankSignals.RecencyBand(0, Now + Hour, Now));
        Assert.AreEqual(RankSignals.BandOlder, RankSignals.RecencyBand(0, Now + 365 * Day, Now));
    }

    // FileMetadata's "not recorded" is DateTime.MinValue, which must not throw on conversion.
    [TestMethod]
    public void RecencyBand_DateTimeOverload_HandlesMissingAndLocalTimes()
    {
        var now = DateTime.UtcNow;
        Assert.AreEqual(RankSignals.BandOlder, RankSignals.RecencyBand(DateTime.MinValue, DateTime.MinValue, now));
        Assert.AreEqual(RankSignals.BandToday, RankSignals.RecencyBand(DateTime.MinValue, now.ToLocalTime().AddMinutes(-5), now));
    }

    [TestMethod]
    [DataRow(@"D:\", 0)]
    [DataRow(@"D:\dev", 1)]
    [DataRow(@"D:\dev\mcptools\code-atlas", 3)]
    [DataRow(@"D:\dev\mcptools\code-atlas\", 3)]
    [DataRow(@"\\server\share\atlas.png", 4)]
    public void PathDepth_CountsFoldersBelowTheRoot(string path, int expected) =>
        Assert.AreEqual(expected, RankSignals.PathDepth(path));
}
