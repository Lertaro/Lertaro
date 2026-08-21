using System.Runtime.InteropServices;
using Lertaro.Core.Services.Everything;

namespace Lertaro.Core.Tests.Services.Everything;

[TestClass]
public class EverythingIpcMessageDispatcherTests
{
    private sealed class FakeEverythingDataProvider : IEverythingDataProvider
    {
        public Dictionary<string, uint> History = new(StringComparer.OrdinalIgnoreCase);

        public Task<EverythingQueryResult> ExecuteQueryAsync(EverythingQueryRequest request, CancellationToken token = default)
        {
            var list = new List<EverythingResultItem>
            {
                new(@"C:\Test", "app.exe", 5000, false)
            };
            return Task.FromResult(new EverythingQueryResult(list, 1, 0, 1));
        }

        public uint GetRunCount(string fileName) =>
            History.TryGetValue(fileName, out var count) ? count : 0;

        public void SetRunCount(string fileName, uint count) =>
            History[fileName] = count;

        public uint IncrementRunCount(string fileName)
        {
            var count = GetRunCount(fileName) + 1;
            History[fileName] = count;
            return count;
        }
    }

    [TestMethod]
    public void HandleIpcCommand_VersionAndLoadedProbes_ReturnsExpectedValues()
    {
        var fake = new FakeEverythingDataProvider();
        var dispatcher = new EverythingIpcMessageDispatcher(fake);

        Assert.AreEqual((IntPtr)1, dispatcher.HandleIpcCommand(EverythingIpcConstants.IpcGetMajorVersion, IntPtr.Zero));
        Assert.AreEqual((IntPtr)4, dispatcher.HandleIpcCommand(EverythingIpcConstants.IpcGetMinorVersion, IntPtr.Zero));
        Assert.AreEqual((IntPtr)1300, dispatcher.HandleIpcCommand(EverythingIpcConstants.IpcGetBuildNumber, IntPtr.Zero));
        Assert.AreEqual((IntPtr)1, dispatcher.HandleIpcCommand(EverythingIpcConstants.IpcIsDbLoaded, IntPtr.Zero));
        Assert.AreEqual(IntPtr.Zero, dispatcher.HandleIpcCommand(EverythingIpcConstants.IpcIsDbBusy, IntPtr.Zero));
        Assert.AreEqual((IntPtr)1, dispatcher.HandleIpcCommand(EverythingIpcConstants.IpcIsNtfsDriveIndexed, IntPtr.Zero));
    }

    [TestMethod]
    public void HandleIpcCommand_FileInfoIndexed_ReturnsSupportedForFolderSizeAndDate()
    {
        var fake = new FakeEverythingDataProvider();
        var dispatcher = new EverythingIpcMessageDispatcher(fake);

        Assert.AreEqual((IntPtr)1, dispatcher.HandleIpcCommand(EverythingIpcConstants.IpcIsFileInfoIndexed, (IntPtr)EverythingIpcConstants.FileInfoFolderSize));
        Assert.AreEqual((IntPtr)1, dispatcher.HandleIpcCommand(EverythingIpcConstants.IpcIsFileInfoIndexed, (IntPtr)EverythingIpcConstants.FileInfoFileSize));
        Assert.AreEqual((IntPtr)1, dispatcher.HandleIpcCommand(EverythingIpcConstants.IpcIsFileInfoIndexed, (IntPtr)EverythingIpcConstants.FileInfoDateModified));
    }

    [TestMethod]
    public void HandleCopyData_RunCountMessages_UpdatesAndReturnsCounts()
    {
        var fake = new FakeEverythingDataProvider();
        var dispatcher = new EverythingIpcMessageDispatcher(fake);

        var filePath = @"C:\Test\notepad.exe";
        var strBytes = System.Text.Encoding.Unicode.GetBytes(filePath + "\0");

        // 1. Initial count check (should be 0)
        var buffer = Marshal.AllocHGlobal(strBytes.Length);
        try
        {
            Marshal.Copy(strBytes, 0, buffer, strBytes.Length);
            var cds = new CopyDataStructWrapper
            {
                dwData = (IntPtr)EverythingIpcConstants.CopyDataGetRunCountW,
                cbData = strBytes.Length,
                lpData = buffer
            };
            var cdsPtr = Marshal.AllocHGlobal(Marshal.SizeOf<CopyDataStructWrapper>());
            try
            {
                Marshal.StructureToPtr(cds, cdsPtr, false);
                var result = dispatcher.HandleCopyData(IntPtr.Zero, cdsPtr, IntPtr.Zero);
                Assert.AreEqual(IntPtr.Zero, result);
            }
            finally
            {
                Marshal.FreeHGlobal(cdsPtr);
            }

            // 2. Increment count (should return 1)
            var cdsInc = new CopyDataStructWrapper
            {
                dwData = (IntPtr)EverythingIpcConstants.CopyDataIncRunCountW,
                cbData = strBytes.Length,
                lpData = buffer
            };
            var cdsIncPtr = Marshal.AllocHGlobal(Marshal.SizeOf<CopyDataStructWrapper>());
            try
            {
                Marshal.StructureToPtr(cdsInc, cdsIncPtr, false);
                var incResult = dispatcher.HandleCopyData(IntPtr.Zero, cdsIncPtr, IntPtr.Zero);
                Assert.AreEqual((IntPtr)1, incResult);
            }
            finally
            {
                Marshal.FreeHGlobal(cdsIncPtr);
            }

            Assert.AreEqual(1u, fake.GetRunCount(filePath));
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CopyDataStructWrapper
    {
        public IntPtr dwData;
        public int cbData;
        public IntPtr lpData;
    }
}
