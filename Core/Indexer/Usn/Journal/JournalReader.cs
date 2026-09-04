using System.Runtime.InteropServices;
using Lertaro.Core.Indexer.Mft;

using Lertaro.Core.DriveMonitoring;
using Lertaro.Core.Indexer.NetworkDrive.Walk;
namespace Lertaro.Core.Indexer.Usn.Journal;

public class JournalReader
{
    // token, previousStore, and onCheckpoint only apply to the ReFS branch below -- MftIndexScanner (raw
    // $MFT parse, not a walk) has no natural interruption point and no diff-reuse/checkpoint capability,
    // all out of scope for the same reason: it's not a walk. A Stop request, a previous store's baseline,
    // or mid-walk checkpoint publishing are all no-ops for NTFS.
    internal UsnDriveIndexResult? IndexDrive(
        string drive,
        FileRecordStore? previousStore = null,
        Action<int, int>? onProgress = null,
        Action<FileRecordStore, NetworkDriveWalkStats>? onCheckpoint = null,
        CancellationToken token = default)
    {
        Logger.Log($"[JournalReader] Indexing drive {drive}...");
        var volumePath = $"\\\\.\\{drive}:";
        using var handle = Win32Api.CreateFileW(
            volumePath,
            Win32Api.GENERIC_READ,
            Win32Api.FILE_SHARE_READ | Win32Api.FILE_SHARE_WRITE,
            IntPtr.Zero,
            Win32Api.OPEN_EXISTING,
            0,
            IntPtr.Zero);
        if (handle.IsInvalid)
        {
            Logger.Log($"[JournalReader] Failed to open drive {drive} handle.", LogLevel.Error);
            return null;
        }
        var fsType = VolumeHelper.GetFileSystemType(drive);
        var rootFrn = VolumeHelper.GetRootFrn(drive);
        if (!rootFrn.HasValue)
        {
            Logger.Log($"[JournalReader] Failed to resolve root FRN on {drive}.", LogLevel.Error);
            return null;
        }
        var queryBuf = new byte[56];
        var success = Win32Api.DeviceIoControl(
            handle,
            Win32Api.FSCTL_QUERY_USN_JOURNAL,
            IntPtr.Zero, 0,
            queryBuf, (uint)queryBuf.Length,
            out var bytesReturned,
            IntPtr.Zero);
        if (!success)
        {
            var err = Marshal.GetLastWin32Error();
            fsType = VolumeHelper.GetFileSystemType(drive);
            Logger.Log($"[JournalReader] Failed to query USN journal on {drive}. Error: {err}, FileSystem: {fsType}", LogLevel.Warn);

            if (VolumeHelper.IsJournalCapableFileSystem(fsType))
            {
                Logger.Log($"[JournalReader] Attempting to create/activate USN journal on {fsType} drive {drive}...");
                var createData = new Win32Api.CREATE_USN_JOURNAL_DATA
                {
                    MaximumSize = 0,
                    AllocationDelta = 0
                };
                var createSuccess = Win32Api.DeviceIoControl(
                    handle,
                    Win32Api.FSCTL_CREATE_USN_JOURNAL,
                    ref createData, (uint)Marshal.SizeOf<Win32Api.CREATE_USN_JOURNAL_DATA>(),
                    IntPtr.Zero, 0,
                    out var bytesReturnedCreate,
                    IntPtr.Zero);

                if (createSuccess)
                {
                    Logger.Log($"[JournalReader] USN journal successfully created/activated on {drive}. Retrying query...");
                    success = Win32Api.DeviceIoControl(
                        handle,
                        Win32Api.FSCTL_QUERY_USN_JOURNAL,
                        IntPtr.Zero, 0,
                        queryBuf, (uint)queryBuf.Length,
                        out bytesReturned,
                        IntPtr.Zero);
                }
                else
                {
                    var createErr = Marshal.GetLastWin32Error();
                    Logger.Log($"[JournalReader] Failed to create USN journal on {drive}. Error: {createErr}", LogLevel.Error);
                }
            }
        }

        if (!success)
        {
            Logger.Log($"[JournalReader] Failed to query USN journal on {drive}.", LogLevel.Error);
            return null;
        }
        var journalId = BitConverter.ToUInt64(queryBuf, 0);
        var nextUsn = BitConverter.ToInt64(queryBuf, 16);

        if (fsType.Equals("ReFS", StringComparison.OrdinalIgnoreCase))
        {
            return ReFsScanner.ScanDrive(drive, handle, rootFrn.Value, journalId, nextUsn, previousStore, onProgress, onCheckpoint, token);
        }

        // NTFS: parse the raw $MFT so hard links are fully indexed (one row per link).
        var mftResult = MftIndexScanner.ScanDrive(drive, handle, rootFrn.Value, journalId, nextUsn, onProgress);
        if (mftResult == null)
            Logger.Log($"[JournalReader] $MFT scan failed on {drive}; drive not indexed.", LogLevel.Error);
        return mftResult;
    }

    public long CatchUpDrive(string drive, ulong journalId, long startUsn, Func<ParsedUsnRecord, bool> onRecord) => JournalReaderHelper.CatchUpDrive(drive, journalId, startUsn, onRecord);
}
