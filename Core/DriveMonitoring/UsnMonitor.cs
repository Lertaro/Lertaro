using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Lertaro.Core.Indexer.Usn;

namespace Lertaro.Core.DriveMonitoring;

public class UsnMonitor
{
    private readonly string _drive;
    private readonly ulong _journalId;
    private long _startUsn;
    private readonly UsnIndexer _indexer;
    private readonly CancellationToken _token;
    private readonly Action<string>? _onReindexRequired;

    public UsnMonitor(
        string drive,
        ulong journalId,
        long startUsn,
        UsnIndexer indexer,
        CancellationToken token,
        Action<string>? onReindexRequired = null)
    {
        _drive = drive;
        _journalId = journalId;
        _startUsn = startUsn;
        _indexer = indexer;
        _token = token;
        _onReindexRequired = onReindexRequired;
    }

    public void Start() => Task.Run(async () =>
                                {
                                    try
                                    {
                                        await MonitorLoop();
                                    }
                                    catch (OperationCanceledException)
                                    {
                                        Logger.Log($"[Monitor] Monitoring on drive {_drive} cancelled successfully.");
                                    }
                                    catch (Exception ex)
                                    {
                                        Logger.Log($"[Monitor] Critical loop failure on drive {_drive}: {ex}", LogLevel.Error);
                                    }
                                }, _token);

    private async Task MonitorLoop()
    {
        Logger.Log($"[Monitor] Started real-time monitoring on drive {_drive} from USN {_startUsn}...");
        var volumePath = $"\\\\.\\{_drive}:";
        var handle = OpenVolume(volumePath);
        if (handle.IsInvalid)
        {
            Logger.Log($"[Monitor] Failed to open drive {_drive} handle for monitoring.", LogLevel.Error);
            handle.Dispose();
            return;
        }

        var outBuf = new byte[64 * 1024];
        var consecutiveUnexpectedErrors = 0;

        try
        {
            while (!_token.IsCancellationRequested)
            {
                try
                {
                    var previousUsn = _startUsn;
                    var input = new Win32Api.READ_USN_JOURNAL_DATA_V0
                    {
                        StartUsn = _startUsn,
                        ReasonMask = 0xFFFFFFFF,
                        ReturnOnlyOnClose = 0,
                        Timeout = 0,
                        BytesToWaitFor = 0,
                        UsnJournalID = _journalId
                    };

                    var success = Win32Api.DeviceIoControl(
                        handle,
                        Win32Api.FSCTL_READ_USN_JOURNAL,
                        ref input, (uint)Marshal.SizeOf<Win32Api.READ_USN_JOURNAL_DATA_V0>(),
                        outBuf, (uint)outBuf.Length,
                        out var bytesReturned,
                        IntPtr.Zero);

                    if (!success)
                    {
                        var err = Marshal.GetLastWin32Error();
                        Logger.Log($"[Monitor] FSCTL_READ_USN_JOURNAL error on drive {_drive}: {err}", LogLevel.Warn);
                        handle = await RecoverVolumeHandle(volumePath, handle, err);
                        if (handle.IsInvalid)
                            break;
                        consecutiveUnexpectedErrors = 0;
                        continue;
                    }

                    consecutiveUnexpectedErrors = 0;
                    await ProcessReadBuffer(outBuf, (int)bytesReturned, previousUsn);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Logger.Log($"[Monitor] Unexpected error in monitor loop on drive {_drive}: {ex}", LogLevel.Error);
                    await Task.Delay(2000, _token);
                    consecutiveUnexpectedErrors++;
                    if (consecutiveUnexpectedErrors > 10)
                    {
                        Logger.Log($"[Monitor] Too many consecutive unexpected errors on drive {_drive}. Stopping monitor.", LogLevel.Error);
                        break;
                    }
                }
            }
        }
        finally
        {
            handle.Dispose();
        }

        Logger.Log($"[Monitor] Stopped monitoring on drive {_drive}.");
    }

    private async Task ProcessReadBuffer(byte[] outBuf, int returnedSize, long previousUsn)
    {
        if (returnedSize > 8)
        {
            var nextUsn = BitConverter.ToInt64(outBuf, 0);
            var offset = 8;
            var recordsProcessed = 0;
            var records = new List<ParsedUsnRecord>();

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
                    records.Add(UsnRecordParser.ParseRecord(recordSpan));
                    recordsProcessed++;
                }
                catch (Exception ex)
                {
                    Logger.Log($"[Monitor] Record parse error during monitoring on {_drive}: {ex}", LogLevel.Error);
                }

                offset += (int)recordLen;
            }

            // Advance the watermark only after a successful apply. If ApplyUsnRecords throws, keep the
            // old _startUsn so the next read replays this batch instead of dropping it forever.
            if (records.Count > 0)
            {
                _indexer.ApplyUsnRecords(_drive, records);
                _startUsn = nextUsn;
            }
            else if (recordsProcessed == 0)
            {
                _startUsn = nextUsn;
            }

            if (_startUsn == previousUsn)
                await Task.Delay(1000, _token);
            else if (recordsProcessed == 0)
                await Task.Delay(200, _token);
            return;
        }

        if (returnedSize == 8)
            _startUsn = BitConverter.ToInt64(outBuf, 0);

        await Task.Delay(500, _token);
    }

    private static SafeFileHandle OpenVolume(string volumePath) => Win32Api.CreateFileW(
        volumePath,
        Win32Api.GENERIC_READ,
        Win32Api.FILE_SHARE_READ | Win32Api.FILE_SHARE_WRITE,
        IntPtr.Zero,
        Win32Api.OPEN_EXISTING,
        0,
        IntPtr.Zero);

    private async Task<SafeFileHandle> RecoverVolumeHandle(string volumePath, SafeFileHandle oldHandle, int lastError)
    {
        oldHandle.Dispose();

        if (!IsRecoverableVolumeError(lastError))
        {
            Logger.Log($"[Monitor] Non-recoverable USN journal error on drive {_drive}: {lastError}. Stopping monitor.", LogLevel.Error);
            return new SafeFileHandle(IntPtr.Zero, ownsHandle: true);
        }

        for (var attempt = 1; attempt <= 30 && !_token.IsCancellationRequested; attempt++)
        {
            await Task.Delay(2000, _token);
            var handle = OpenVolume(volumePath);
            if (handle.IsInvalid)
            {
                handle.Dispose();
                if (attempt == 1 || attempt % 5 == 0)
                    Logger.Log($"[Monitor] Drive {_drive} not ready; reopen attempt {attempt}/30.", LogLevel.Warn);
                continue;
            }

            var journal = QueryJournal(handle);
            if (journal.HasValue && journal.Value.JournalId == _journalId)
            {
                if (_startUsn < journal.Value.LowestValidUsn || _startUsn > journal.Value.NextUsn)
                {
                    handle.Dispose();
                    Logger.Log($"[Monitor] USN {_startUsn} on drive {_drive} is outside journal range {journal.Value.LowestValidUsn}..{journal.Value.NextUsn}. Stopping monitor for re-index.", LogLevel.Error);
                    _onReindexRequired?.Invoke(_drive);
                    return new SafeFileHandle(IntPtr.Zero, ownsHandle: true);
                }

                Logger.Log($"[Monitor] Reopened drive {_drive}; resuming from USN {_startUsn}.");
                return handle;
            }

            handle.Dispose();
            if (journal.HasValue)
            {
                Logger.Log($"[Monitor] Journal ID changed on drive {_drive}. Stopping monitor for re-index.", LogLevel.Error);
                _onReindexRequired?.Invoke(_drive);
                return new SafeFileHandle(IntPtr.Zero, ownsHandle: true);
            }
        }

        Logger.Log($"[Monitor] Drive {_drive} did not recover after reopen attempts. Stopping monitor.", LogLevel.Error);
        return new SafeFileHandle(IntPtr.Zero, ownsHandle: true);
    }

    private static bool IsRecoverableVolumeError(int err) =>
        err is Win32Api.ERROR_NOT_READY or Win32Api.ERROR_INVALID_HANDLE or Win32Api.ERROR_DEVICE_NOT_CONNECTED;

    private static (ulong JournalId, long LowestValidUsn, long NextUsn)? QueryJournal(SafeFileHandle handle)
    {
        var queryBuf = new byte[56];
        var success = Win32Api.DeviceIoControl(
            handle,
            Win32Api.FSCTL_QUERY_USN_JOURNAL,
            IntPtr.Zero, 0,
            queryBuf, (uint)queryBuf.Length,
            out _,
            IntPtr.Zero);

        return success
            ? (BitConverter.ToUInt64(queryBuf, 0), BitConverter.ToInt64(queryBuf, 24), BitConverter.ToInt64(queryBuf, 16))
            : null;
    }
}
