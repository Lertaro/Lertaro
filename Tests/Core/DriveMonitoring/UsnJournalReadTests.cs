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

    [TestMethod]
    public void NativeRequestHasVersionFieldsAtV1Offsets()
    {
        Assert.AreEqual(40, Marshal.SizeOf<UsnJournalRead.RequestV0>());
        Assert.AreEqual(48, Marshal.SizeOf<UsnJournalRead.RequestV1>());
        Assert.AreEqual((IntPtr)40, Marshal.OffsetOf<UsnJournalRead.RequestV1>(nameof(UsnJournalRead.RequestV1.MinMajorVersion)));
        Assert.AreEqual((IntPtr)42, Marshal.OffsetOf<UsnJournalRead.RequestV1>(nameof(UsnJournalRead.RequestV1.MaxMajorVersion)));
    }
}
