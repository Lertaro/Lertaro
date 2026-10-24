using Microsoft.Win32.SafeHandles;

namespace Lertaro.Core.DriveMonitoring;

// The three numbers FSCTL_QUERY_USN_JOURNAL returns that decide whether a remembered USN may still be
// read. Offset 8 is FirstUsn, the oldest record the journal still holds; NTFS reports LowestValidUsn
// (offset 24) as 0 for a live journal, so a check against that field always passes and the read it
// guards then fails with 1181 (ERROR_JOURNAL_ENTRY_DELETED) instead -- the misdiagnosis this type exists
// to end.
internal readonly record struct UsnJournalInfo(ulong JournalId, long FirstUsn, long NextUsn)
{
    // USN_JOURNAL_DATA_V0: JournalId, FirstUsn, NextUsn, LowestValidUsn, MaxUsn, MaximumSize,
    // AllocationDelta -- 56 bytes in total.
    internal const int BufferSize = 56;

    internal static UsnJournalInfo FromBuffer(byte[] buffer) =>
        new(BitConverter.ToUInt64(buffer, 0), BitConverter.ToInt64(buffer, 8), BitConverter.ToInt64(buffer, 16));

    internal bool Contains(long usn) => usn >= FirstUsn && usn <= NextUsn;

    // Null when the volume cannot be opened or its journal cannot be queried: callers keep the position
    // they had and let the monitor's own recovery path deal with a drive that stopped answering.
    internal static UsnJournalInfo? TryQuery(string drive)
    {
        using var handle = Win32Api.CreateFileW($"\\\\.\\{drive}:", Win32Api.GENERIC_READ,
            Win32Api.FILE_SHARE_READ | Win32Api.FILE_SHARE_WRITE, IntPtr.Zero, Win32Api.OPEN_EXISTING, 0, IntPtr.Zero);
        return handle.IsInvalid ? null : TryQuery(handle);
    }

    internal static UsnJournalInfo? TryQuery(SafeFileHandle handle)
    {
        var buffer = new byte[BufferSize];
        var success = Win32Api.DeviceIoControl(handle, Win32Api.FSCTL_QUERY_USN_JOURNAL,
            IntPtr.Zero, 0, buffer, (uint)buffer.Length, out _, IntPtr.Zero);
        return success ? FromBuffer(buffer) : null;
    }
}
