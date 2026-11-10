using System.Runtime.InteropServices;
using Lertaro.Core.DriveMonitoring;

namespace Lertaro.Core.Tests.DriveMonitoring;

[TestClass]
public sealed class UsnJournalReadTests
{
    [TestMethod]
    public void ReFsRequestsV3WhileNtfsKeepsV2()
    {
        Assert.AreEqual((ushort)3, UsnJournalRead.RecordVersion("ReFS"));
        Assert.AreEqual((ushort)2, UsnJournalRead.RecordVersion("ntfs"));
        Assert.Throws<NotSupportedException>(() => UsnJournalRead.RecordVersion("exFAT"));
        var v0 = UsnJournalRead.CreateV0(123, 456);
        Assert.AreEqual(123L, v0.StartUsn);
        Assert.AreEqual(456UL, v0.UsnJournalId);
        Assert.AreEqual(uint.MaxValue, v0.ReasonMask);
        var v1 = new UsnJournalRead.RequestV1 { MinMajorVersion = 3, MaxMajorVersion = 3 };
        Assert.AreEqual((ushort)3, v1.MinMajorVersion);
        Assert.AreEqual((ushort)3, v1.MaxMajorVersion);
    }

    // STATIC LAYOUT VERIFICATION, not a runtime check: the test host cannot issue
    // FSCTL_READ_USN_JOURNAL against a real volume, so these assertions pin the managed structs against
    // the winioctl.h definitions (READ_USN_JOURNAL_DATA_V0 / _V1) instead of against the driver.
    //
    // The kernel reads this memory by offset, so a field declared one width too narrow silently shifts
    // every field after it -- the struct still marshals, the ioctl still returns, and only the values are
    // wrong. The trap is that Timeout and BytesToWaitFor read like 32-bit counters but are DWORDLONG;
    // only ReasonMask and ReturnOnlyOnClose are 4 bytes.
    [TestMethod]
    public void NativeRequestMatchesTheWinIoctlLayout()
    {
        Assert.AreEqual(40, Marshal.SizeOf<UsnJournalRead.RequestV0>());
        Assert.AreEqual(48, Marshal.SizeOf<UsnJournalRead.RequestV1>());

        Assert.AreEqual((IntPtr)0, Marshal.OffsetOf<UsnJournalRead.RequestV0>(nameof(UsnJournalRead.RequestV0.StartUsn)));
        Assert.AreEqual((IntPtr)8, Marshal.OffsetOf<UsnJournalRead.RequestV0>(nameof(UsnJournalRead.RequestV0.ReasonMask)));
        Assert.AreEqual((IntPtr)12, Marshal.OffsetOf<UsnJournalRead.RequestV0>(nameof(UsnJournalRead.RequestV0.ReturnOnlyOnClose)));
        Assert.AreEqual((IntPtr)16, Marshal.OffsetOf<UsnJournalRead.RequestV0>(nameof(UsnJournalRead.RequestV0.Timeout)));
        Assert.AreEqual((IntPtr)24, Marshal.OffsetOf<UsnJournalRead.RequestV0>(nameof(UsnJournalRead.RequestV0.BytesToWaitFor)));
        Assert.AreEqual((IntPtr)32, Marshal.OffsetOf<UsnJournalRead.RequestV0>(nameof(UsnJournalRead.RequestV0.UsnJournalId)));
    }

    // V1 is V0 with two WORDs appended, so the shared prefix must keep its offsets and the version fields
    // must sit immediately after it. NTFS reads version 2 through V0 and ReFS version 3 through V1, so
    // both layouts have to hold.
    [TestMethod]
    public void NativeRequestV1AppendsTheVersionWordsWithoutMovingThePrefix()
    {
        Assert.AreEqual((IntPtr)16, Marshal.OffsetOf<UsnJournalRead.RequestV1>(nameof(UsnJournalRead.RequestV1.Timeout)));
        Assert.AreEqual((IntPtr)24, Marshal.OffsetOf<UsnJournalRead.RequestV1>(nameof(UsnJournalRead.RequestV1.BytesToWaitFor)));
        Assert.AreEqual((IntPtr)32, Marshal.OffsetOf<UsnJournalRead.RequestV1>(nameof(UsnJournalRead.RequestV1.UsnJournalId)));
        Assert.AreEqual((IntPtr)40, Marshal.OffsetOf<UsnJournalRead.RequestV1>(nameof(UsnJournalRead.RequestV1.MinMajorVersion)));
        Assert.AreEqual((IntPtr)42, Marshal.OffsetOf<UsnJournalRead.RequestV1>(nameof(UsnJournalRead.RequestV1.MaxMajorVersion)));
    }
}
