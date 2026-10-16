using System.Runtime.InteropServices;
using Lertaro.Core.Services.Installation;
using Microsoft.Win32.SafeHandles;

namespace Lertaro.Core.Tests.Services.Installation;

// What the callers of an open handle get to depend on. The walk never trusts a path twice: each entry is opened
// once, without following reparse points, and everything after that goes through the handle.
[TestClass]
public sealed class DirectoryLockNativeMethodsTests
{
    [TestMethod]
    public void OpenWithoutFollowing_MigrationModeOnALiveDirectory_NamesTheWin32Refusal()
    {
        var root = Path.Combine(Path.GetTempPath(), $"LertaroLiveDirectory_{Guid.NewGuid():N}");
        var directory = Directory.CreateDirectory(Path.Combine(root, "data")).FullName;
        try
        {
            using var live = HoldOpenWithoutSharingDelete(directory);
            Assert.IsFalse(live.IsInvalid, "the test needs a held directory to reproduce the refusal");

            // Migration mode asks for DELETE so a link can be removed through the handle. Every other open of a
            // live directory withholds FILE_SHARE_DELETE, and a Windows share check is symmetric, so the request
            // is refused outright -- which is why pinning a live profile has to ask for something else, and why
            // the message has to name the reason: "Could not open 'C:\Users\<you>'." on its own sent a bug
            // report to `%TEMP%\Lertaro\repair.log` with nothing in it to act on.
            var refused = Assert.ThrowsExactly<IOException>(
                () => DirectoryLockNativeMethods.OpenWithoutFollowing(directory, migration: true));

            StringAssert.Contains(refused.Message, "Win32 32");
            Assert.IsNotNull(refused.InnerException);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    /// <summary>How the shell opens a directory: reads and writes shared, delete withheld.</summary>
    internal static SafeFileHandle HoldOpenWithoutSharingDelete(string path) => CreateFileW(path,
        FileListDirectory | Synchronize, ShareRead | ShareWrite, IntPtr.Zero, OpenExisting,
        BackupSemantics | OpenReparsePoint, IntPtr.Zero);

    private const uint FileListDirectory = 0x00000001, Synchronize = 0x00100000;
    private const uint ShareRead = 0x00000001, ShareWrite = 0x00000002;
    private const uint OpenExisting = 3, BackupSemantics = 0x02000000, OpenReparsePoint = 0x00200000;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security,
        uint disposition, uint flags, IntPtr template);
}
