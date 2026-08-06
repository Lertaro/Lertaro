using System.Runtime.InteropServices;

using Lertaro.Core.DriveMonitoring;
namespace Lertaro.Core.Indexer.Usn.Journal;

public static class JournalReaderHelper
{
    public static long CatchUpDrive(string drive, ulong journalId, long startUsn, Action<ParsedUsnRecord> onRecord)
    {
        Logger.Log($"[JournalReaderHelper] Catching up drive {drive} from USN {startUsn}...");
        var volumePath = $"\\\\.\\{drive}:";
        using var handle = Win32Api.CreateFileW(
            volumePath,
            Win32Api.GENERIC_READ,
            Win32Api.FILE_SHARE_READ | Win32Api.FILE_SHARE_WRITE,
            IntPtr.Zero,
            Win32Api.OPEN_EXISTING,
            0,
            IntPtr.Zero
        );

        if (handle.IsInvalid)
        {
            Logger.Log($"[JournalReaderHelper] Failed to open drive {drive} handle for catch-up.", LogLevel.Error);
            return -1;
        }

        var queryBuf = new byte[56];
        var success = Win32Api.DeviceIoControl(
            handle,
            Win32Api.FSCTL_QUERY_USN_JOURNAL,
            IntPtr.Zero, 0,
            queryBuf, (uint)queryBuf.Length,
            out var bytesReturned,
            IntPtr.Zero
        );

        if (!success)
        {
            Logger.Log($"[JournalReaderHelper] Failed to query USN journal for catch-up on {drive}.", LogLevel.Error);
            return -1;
        }

        var currentJournalId = BitConverter.ToUInt64(queryBuf, 0);
        var currentNextUsn = BitConverter.ToInt64(queryBuf, 16);
        var lowestValidUsn = BitConverter.ToInt64(queryBuf, 24);

        if (currentJournalId != journalId)
        {
            Logger.Log($"[JournalReaderHelper] Journal ID mismatch on {drive} (expected {journalId}, got {currentJournalId}). Need full re-index.", LogLevel.Warn);
            return -1;
        }

        if (startUsn < lowestValidUsn || startUsn > currentNextUsn)
        {
            Logger.Log($"[JournalReaderHelper] Cached USN {startUsn} on {drive} is outside journal range {lowestValidUsn}..{currentNextUsn}. Need full re-index.", LogLevel.Warn);
            return -1;
        }

        var currentUsn = startUsn;
        var bufSize = 256 * 1024;
        var outBuf = new byte[bufSize];

        var changeCount = 0;

        while (currentUsn < currentNextUsn)
        {
            var input = new Win32Api.READ_USN_JOURNAL_DATA_V0
            {
                StartUsn = currentUsn,
                ReasonMask = 0xFFFFFFFF,
                ReturnOnlyOnClose = 0,
                Timeout = 0,
                BytesToWaitFor = 0,
                UsnJournalID = journalId
            };

            success = Win32Api.DeviceIoControl(
                handle,
                Win32Api.FSCTL_READ_USN_JOURNAL,
                ref input, (uint)Marshal.SizeOf<Win32Api.READ_USN_JOURNAL_DATA_V0>(),
                outBuf, (uint)outBuf.Length,
                out bytesReturned,
                IntPtr.Zero
            );

            if (!success)
            {
                var err = Marshal.GetLastWin32Error();
                Logger.Log($"[JournalReaderHelper] FSCTL_READ_USN_JOURNAL failed during catch-up on {drive}: {err}", LogLevel.Error);
                return -1;
            }

            var returnedSize = (int)bytesReturned;
            if (returnedSize <= 8)
                break;

            currentUsn = BitConverter.ToInt64(outBuf, 0);
            var offset = 8;

            while (offset < returnedSize)
            {
                if (offset + 4 > returnedSize)
                    break;

                var recordLen = BitConverter.ToUInt32(outBuf, offset);
                if (recordLen == 0 || offset + recordLen > returnedSize)
                    break;

                var recordSpan = new ReadOnlySpan<byte>(outBuf, offset, (int)recordLen);
                try
                {
                    var record = UsnRecordParser.ParseRecord(recordSpan);
                    changeCount++;
                    onRecord(record);
                }
                catch (Exception ex)
                {
                    Logger.Log($"[JournalReaderHelper] Catch-up record parse error on {drive}: {ex}", LogLevel.Error);
                }

                offset += (int)recordLen;
            }
        }

        Logger.Log($"[JournalReaderHelper] Catch-up complete for drive {drive}. Processed {changeCount} changes. Next USN: {currentUsn}");
        return currentUsn;
    }
}
