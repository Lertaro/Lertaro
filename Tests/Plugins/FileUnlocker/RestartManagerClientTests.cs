using System;
using System.Runtime.InteropServices;

namespace Lertaro.Plugins.FileUnlocker.Tests;

// Pins the managed RM_PROCESS_INFO declaration to the native restartmanager.h layout. The struct
// previously carried a stray ProcessStartTime field and ordered the inline strings after the
// scalars, so every RMGetList record past the first read back shifted: wrong PIDs, truncated
// process names, garbage application types.
[TestClass]
public sealed class RestartManagerClientTests
{
    [TestMethod]
    public void RmProcessInfo_Size_MatchesNativeLayout()
    {
        // restartmanager.h: RM_UNIQUE_PROCESS (DWORD id + FILETIME, 12 bytes: FILETIME is two
        // DWORDs and carries 4-byte alignment, so there is no x64 padding), then 256 + 64 inline
        // WCHARs, then four 32-bit scalars/flags (ApplicationType, AppStatus, TSSessionId,
        // BOOL bRestartable) -- 668 bytes on every architecture.
        Assert.AreEqual(668, Marshal.SizeOf<RestartManagerClient.RM_PROCESS_INFO>());
    }

    [TestMethod]
    public void RmProcessInfo_StringsStartRightAfterTheProcessBlock()
    {
        Assert.AreEqual((IntPtr)12, Marshal.OffsetOf<RestartManagerClient.RM_PROCESS_INFO>(nameof(RestartManagerClient.RM_PROCESS_INFO.strAppName)));
    }

    [TestMethod]
    public void RmUniqueProcess_Size_MatchesNativeLayout()
    {
        // DWORD dwProcessId + FILETIME (two DWORDs), 4-byte aligned: 12 bytes, no padding.
        Assert.AreEqual(12, Marshal.SizeOf<RestartManagerClient.RM_UNIQUE_PROCESS>());
    }
}
