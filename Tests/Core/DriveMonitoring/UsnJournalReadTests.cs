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
        var request = UsnJournalRead.Create(123, 456, 3);
        Assert.AreEqual(123L, request.StartUsn);
        Assert.AreEqual(456UL, request.UsnJournalId);
        Assert.AreEqual(uint.MaxValue, request.ReasonMask);
        Assert.AreEqual((ushort)3, request.MinMajorVersion);
        Assert.AreEqual((ushort)3, request.MaxMajorVersion);
    }

    [TestMethod]
    public void NativeRequestHasVersionFieldsAtV1Offsets()
    {
        Assert.AreEqual(48, Marshal.SizeOf<UsnJournalRead.Request>());
        Assert.AreEqual((IntPtr)40, Marshal.OffsetOf<UsnJournalRead.Request>(nameof(UsnJournalRead.Request.MinMajorVersion)));
        Assert.AreEqual((IntPtr)42, Marshal.OffsetOf<UsnJournalRead.Request>(nameof(UsnJournalRead.Request.MaxMajorVersion)));
    }
}
