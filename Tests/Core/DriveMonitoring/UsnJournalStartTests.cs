using Lertaro.Core.DriveMonitoring;

namespace Lertaro.Core.Tests.DriveMonitoring;

// A monitor that starts at a USN the journal already deleted never makes progress: every read answers
// 1181, the monitor asks for a re-index, and the rebuild hands the next monitor the very same dead
// position -- a full $MFT scan and a multi-hundred-MB snapshot rewrite per turn, forever. These pin the
// decision that breaks the loop, and the buffer offsets it reads.
[TestClass]
public sealed class UsnJournalStartTests
{
    private const ulong JournalId = 0x01dd3abc80a8ad75;

    [TestMethod]
    public void Resolve_PositionStillInsideTheJournal_IsKeptAsIs()
    {
        var decision = UsnJournalStart.Resolve(1500, JournalId, new UsnJournalInfo(JournalId, 1000, 2000));

        Assert.AreEqual(1500L, decision.StartUsn);
        Assert.IsFalse(decision.SkippedGap);
    }

    [TestMethod]
    public void Resolve_PositionAlreadyWrapped_ResumesFromTheJournalTail()
    {
        var decision = UsnJournalStart.Resolve(999, JournalId, new UsnJournalInfo(JournalId, 1000, 2000));

        Assert.AreEqual(2000L, decision.StartUsn);
        Assert.IsTrue(decision.SkippedGap);
    }

    [TestMethod]
    public void Resolve_PositionAheadOfTheJournal_ResumesFromTheJournalTail()
    {
        var decision = UsnJournalStart.Resolve(2001, JournalId, new UsnJournalInfo(JournalId, 1000, 2000));

        Assert.AreEqual(2000L, decision.StartUsn);
        Assert.IsTrue(decision.SkippedGap);
    }

    // A journal ID mismatch is deliberately NOT clamped: UsnMonitor's own ID check has to see the stamp
    // untouched to ask for the re-index that a genuinely recreated journal needs.
    [TestMethod]
    public void Resolve_DifferentJournal_LeavesTheDecisionToTheMonitor()
    {
        var decision = UsnJournalStart.Resolve(999, JournalId + 1, new UsnJournalInfo(JournalId, 1000, 2000));

        Assert.AreEqual(999L, decision.StartUsn);
        Assert.IsFalse(decision.SkippedGap);
    }

    [TestMethod]
    public void Resolve_BothWindowEdges_AreStillReadable()
    {
        var journal = new UsnJournalInfo(JournalId, 1000, 2000);

        Assert.IsFalse(UsnJournalStart.Resolve(1000, JournalId, journal).SkippedGap);
        Assert.IsFalse(UsnJournalStart.Resolve(2000, JournalId, journal).SkippedGap);
    }

    // Offset 8, not 24: LowestValidUsn (24) reads 0 on a live NTFS journal, which is exactly why the old
    // range checks passed for positions that had already been deleted -- the 1181 one millisecond later.
    [TestMethod]
    public void FromBuffer_ReadsFirstUsnFromOffsetEight()
    {
        var buffer = new byte[UsnJournalInfo.BufferSize];
        BitConverter.GetBytes(JournalId).CopyTo(buffer, 0);
        BitConverter.GetBytes(1000L).CopyTo(buffer, 8);
        BitConverter.GetBytes(2000L).CopyTo(buffer, 16);
        BitConverter.GetBytes(0L).CopyTo(buffer, 24); // LowestValidUsn, zeroed like NTFS reports it

        var info = UsnJournalInfo.FromBuffer(buffer);

        Assert.AreEqual(JournalId, info.JournalId);
        Assert.AreEqual(1000L, info.FirstUsn);
        Assert.AreEqual(2000L, info.NextUsn);
        Assert.IsFalse(info.Contains(999));
        Assert.IsTrue(info.Contains(1000));
        Assert.IsTrue(info.Contains(2000));
        Assert.IsFalse(info.Contains(2001));
    }
}
