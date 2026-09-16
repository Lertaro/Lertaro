using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Lertaro.Core.DriveMonitoring;

internal static class UsnJournalRead
{
    // These two structs mirror READ_USN_JOURNAL_DATA_V0 / _V1 from winioctl.h and are passed straight to
    // FSCTL_READ_USN_JOURNAL, so their field order, widths and padding must match the SDK exactly --
    // the kernel reads this memory by offset, and a wrong width silently shifts every field after it.
    //
    // The widths are not the ones the names suggest, which is the trap: Timeout and BytesToWaitFor are
    // DWORDLONG (8 bytes), NOT DWORD, even though both are "just" millisecond/byte counts. UsnJournalId
    // is DWORDLONG too, and StartUsn is the USN typedef (LONGLONG). Only ReasonMask and
    // ReturnOnlyOnClose are actually 4 bytes.
    //
    // Layout is 8-byte aligned with no interior padding: 8+4+4+8+8+8 = 40 bytes for V0, and V1 appends
    // two WORDs for 44 -> 48 after the trailing alignment. NTFS reads journal version 2 through V0;
    // ReFS reads version 3 through V1, which is what exposes the 128-bit record ids. UsnJournalReadTests
    // pins the sizes and offsets so this cannot drift silently.
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
