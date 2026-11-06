using Lertaro.Core.Services;

namespace Lertaro.Core.Tests.Services;

[TestClass]
public sealed class ElevationManagerTests
{
    [TestMethod]
    public void IntegrityNamesTheFourLabelsAndPassesAnythingElseThrough()
    {
        Assert.AreEqual("Low", ElevationManager.IntegrityName(0x1000));
        Assert.AreEqual("Medium", ElevationManager.IntegrityName(0x2000));
        Assert.AreEqual("High", ElevationManager.IntegrityName(0x3000));
        Assert.AreEqual("System", ElevationManager.IntegrityName(0x4000));
        Assert.AreEqual("0x2500", ElevationManager.IntegrityName(0x2500));
    }

    [TestMethod]
    public void HighIsTheRidgeWhereIsHighRidTurns()
    {
        Assert.IsFalse(ElevationManager.IsHighRid(0x2000));
        Assert.IsTrue(ElevationManager.IsHighRid(0x3000));
        Assert.IsTrue(ElevationManager.IsHighRid(0x4000));
    }

    // Reading your own token cannot legitimately fail, so this is where the buffer-size trap shows up: sizing
    // the buffer for TOKEN_MANDATORY_LABEL alone answers ERROR_BAD_LENGTH, and the reader then reports every
    // process -- an elevated one included -- as not High. A level outside the four means the plumbing broke.
    [TestMethod]
    public void TheOwnProcessReportsALevelAtAll()
    {
        var rid = ElevationManager.TryGetIntegrityRid(Environment.ProcessId);
        Assert.IsTrue(rid.HasValue, "reading your own token cannot legitimately fail");
        CollectionAssert.Contains(new[] { 0x1000, 0x2000, 0x3000, 0x4000 }, rid.Value);
    }

    // The distinction the UI depends on: an unreadable process must answer null, not 0, or a caller that only
    // speaks what it measured would report level 0 as if it had been read.
    [TestMethod]
    public void AnUnreadableProcessReportsNoLevelAtAll()
        => Assert.IsNull(ElevationManager.TryGetIntegrityRid(0));

    // PID 0 is the System Idle Process, which no token may open: the answer has to be "I could not read it",
    // not the "not High" that a silent failure would otherwise pass off as a measurement.
    [TestMethod]
    public void AnUnknownProcessIsReportedAsUnreadableRatherThanAsALevel()
        => Assert.IsTrue(ElevationManager.DescribeProcessIntegrity(0).StartsWith("unreadable (", StringComparison.Ordinal));
}
