using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Lertaro.Core.DriveMonitoring;

internal static class UsnJournalRead
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct Request
    {
        public long StartUsn;
        public uint ReasonMask;
        public uint ReturnOnlyOnClose;
        public ulong Timeout;
        public ulong BytesToWaitFor;
        public ulong UsnJournalId;
        public ushort MinMajorVersion;
        public ushort MaxMajorVersion;
    }

    internal static ushort RecordVersion(string fileSystem) => fileSystem.ToUpperInvariant() switch
    {
        "REFS" => 3,
        "NTFS" => 2,
        _ => throw new NotSupportedException($"USN indexing does not support {fileSystem}.")
    };

    internal static Request Create(long startUsn, ulong journalId, ushort version) => new()
    {
        StartUsn = startUsn, ReasonMask = uint.MaxValue, UsnJournalId = journalId,
        // V0 requests expose ReFS compatibility IDs, not the FILE_ID_128 IDs used by the index.
        MinMajorVersion = version, MaxMajorVersion = version
    };

    internal static bool Read(SafeFileHandle handle, long startUsn, ulong journalId, ushort version, byte[] output, out uint returned)
    {
        var request = Create(startUsn, journalId, version);
        return DeviceIoControl(handle, Win32Api.FSCTL_READ_USN_JOURNAL, ref request, (uint)Marshal.SizeOf<Request>(),
            output, (uint)output.Length, out returned, IntPtr.Zero);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle handle, uint code, ref Request input, uint inputSize,
        byte[] output, uint outputSize, out uint returned, IntPtr overlapped);
}
