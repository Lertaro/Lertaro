using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Lertaro.Core.DriveMonitoring;

internal static class UsnJournalRead
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct RequestV0
    {
        public long StartUsn;
        public uint ReasonMask;
        public uint ReturnOnlyOnClose;
        public ulong Timeout;
        public ulong BytesToWaitFor;
        public ulong UsnJournalId;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RequestV1
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

    internal static RequestV0 CreateV0(long startUsn, ulong journalId) => new()
    {
        StartUsn = startUsn, ReasonMask = uint.MaxValue, UsnJournalId = journalId,
    };

    internal static bool Read(SafeFileHandle handle, long startUsn, ulong journalId, ushort version, byte[] output, out uint returned)
    {
        if (version == 2)
        {
            var request = CreateV0(startUsn, journalId);
            return DeviceIoControl(handle, Win32Api.FSCTL_READ_USN_JOURNAL, ref request, (uint)Marshal.SizeOf<RequestV0>(),
                output, (uint)output.Length, out returned, IntPtr.Zero);
        }

        var v1 = new RequestV1
        {
            StartUsn = startUsn, ReasonMask = uint.MaxValue, UsnJournalId = journalId,
            // V1 requests expose the 128-bit IDs required by ReFS records.
            MinMajorVersion = version, MaxMajorVersion = version
        };
        return DeviceIoControl(handle, Win32Api.FSCTL_READ_USN_JOURNAL, ref v1, (uint)Marshal.SizeOf<RequestV1>(),
            output, (uint)output.Length, out returned, IntPtr.Zero);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle handle, uint code, ref RequestV0 input, uint inputSize,
        byte[] output, uint outputSize, out uint returned, IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle handle, uint code, ref RequestV1 input, uint inputSize,
        byte[] output, uint outputSize, out uint returned, IntPtr overlapped);
}
