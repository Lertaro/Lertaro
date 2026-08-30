using System.Runtime.InteropServices;

namespace Lertaro.Core.Services.Everything;

/// <summary>Dispatches Everything IPC window messages (WM_USER and WM_COPYDATA) to the data provider.</summary>
public sealed class EverythingIpcMessageDispatcher
{
    private readonly IEverythingDataProvider _dataProvider;

    [StructLayout(LayoutKind.Sequential)]
    private struct COPYDATASTRUCT
    {
        public IntPtr dwData;
        public int cbData;
        public IntPtr lpData;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, ref COPYDATASTRUCT lParam);

    public EverythingIpcMessageDispatcher(IEverythingDataProvider dataProvider) => _dataProvider = dataProvider ?? throw new ArgumentNullException(nameof(dataProvider));

    public IntPtr HandleIpcCommand(int command, IntPtr lParam) => command switch
    {
        EverythingIpcConstants.IpcGetMajorVersion => (IntPtr)1,
        EverythingIpcConstants.IpcGetMinorVersion => (IntPtr)4,
        EverythingIpcConstants.IpcGetRevision => (IntPtr)1,
        EverythingIpcConstants.IpcGetBuildNumber => (IntPtr)1300,
        EverythingIpcConstants.IpcExit => (IntPtr)1,
        EverythingIpcConstants.IpcGetTargetMachine => (IntPtr)(Environment.Is64BitProcess
            ? EverythingIpcConstants.TargetMachineX64
            : EverythingIpcConstants.TargetMachineX86),

        EverythingIpcConstants.IpcIsStartMenuShortcuts => (IntPtr)1,
        EverythingIpcConstants.IpcIsQuickLaunchShortcut => (IntPtr)1,
        EverythingIpcConstants.IpcIsDesktopShortcut => (IntPtr)1,
        EverythingIpcConstants.IpcIsFolderContextMenu => (IntPtr)1,
        EverythingIpcConstants.IpcIsRunOnSystemStartup => (IntPtr)1,
        EverythingIpcConstants.IpcIsUrlProtocol => (IntPtr)1,
        EverythingIpcConstants.IpcIsService => (IntPtr)1,

        EverythingIpcConstants.IpcIsNtfsDriveIndexed => (IntPtr)1,
        EverythingIpcConstants.IpcIsDbLoaded => (IntPtr)1,
        EverythingIpcConstants.IpcIsDbBusy => IntPtr.Zero,
        EverythingIpcConstants.IpcIsAdmin => (IntPtr)(ElevationManager.IsRunningAsAdmin() ? 1 : 0),
        EverythingIpcConstants.IpcIsAppData => (IntPtr)1,
        EverythingIpcConstants.IpcRebuildDb => (IntPtr)1,
        EverythingIpcConstants.IpcUpdateAllFolderIndexes => (IntPtr)1,
        EverythingIpcConstants.IpcSaveDb => (IntPtr)1,
        EverythingIpcConstants.IpcSaveRunHistory => (IntPtr)1,
        EverythingIpcConstants.IpcDeleteRunHistory => (IntPtr)1,
        EverythingIpcConstants.IpcIsFastSort => (IntPtr)1,
        EverythingIpcConstants.IpcQueueRebuildDb => (IntPtr)1,

        EverythingIpcConstants.IpcIsFileInfoIndexed => IsFileInfoIndexed(lParam.ToInt32()),

        EverythingIpcConstants.IpcIsMatchCase => IntPtr.Zero,
        EverythingIpcConstants.IpcIsMatchWholeWord => IntPtr.Zero,
        EverythingIpcConstants.IpcIsMatchPath => IntPtr.Zero,
        EverythingIpcConstants.IpcIsMatchDiacritics => IntPtr.Zero,
        EverythingIpcConstants.IpcIsRegex => IntPtr.Zero,
        EverythingIpcConstants.IpcIsFilters => IntPtr.Zero,
        EverythingIpcConstants.IpcIsPreview => IntPtr.Zero,
        EverythingIpcConstants.IpcIsStatusBar => (IntPtr)1,
        EverythingIpcConstants.IpcIsDetails => (IntPtr)1,
        EverythingIpcConstants.IpcGetThumbnailSize => IntPtr.Zero,
        EverythingIpcConstants.IpcGetSort => (IntPtr)EverythingIpcConstants.SortNameAscending,
        EverythingIpcConstants.IpcGetOnTop => IntPtr.Zero,
        EverythingIpcConstants.IpcGetFilter => IntPtr.Zero,
        EverythingIpcConstants.IpcGetFilterIndex => IntPtr.Zero,

        _ => IntPtr.Zero
    };

    private static IntPtr IsFileInfoIndexed(int fileInfoType) => fileInfoType switch
    {
        EverythingIpcConstants.FileInfoFileSize => (IntPtr)1,
        EverythingIpcConstants.FileInfoFolderSize => (IntPtr)1,
        EverythingIpcConstants.FileInfoDateCreated => (IntPtr)1,
        EverythingIpcConstants.FileInfoDateModified => (IntPtr)1,
        EverythingIpcConstants.FileInfoDateAccessed => (IntPtr)1,
        EverythingIpcConstants.FileInfoAttributes => (IntPtr)1,
        _ => IntPtr.Zero
    };

    public IntPtr HandleCopyData(IntPtr wParam, IntPtr lParam, IntPtr serverHwnd)
    {
        if (EverythingQueryParser.TryParseCopyDataQuery(lParam, out var queryRequest) && queryRequest != null)
        {
            ProcessQueryAndReply(queryRequest, serverHwnd);
            return (IntPtr)1;
        }

        if (EverythingQueryParser.TryParseRunHistory(lParam, out var runHistoryRequest) && runHistoryRequest != null)
        {
            return ProcessRunHistory(runHistoryRequest);
        }

        if (EverythingQueryParser.TryParseCommandLine(lParam, out _, out _))
        {
            return (IntPtr)1;
        }

        return IntPtr.Zero;
    }

    private void ProcessQueryAndReply(EverythingQueryRequest request, IntPtr serverHwnd)
    {
        try
        {
            var queryTask = _dataProvider.ExecuteQueryAsync(request);
            // Bound the sync wait with a timeout since Win32 SendMessage is blocking for the caller and a
            // stalled query must not wedge the single message-loop thread forever.
            var queryResult = queryTask.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            Logger.Log($"[EverythingIpc] Query: Search='{request.SearchString}', RequestFlags=0x{request.RequestFlags:X}, ReplyHwnd=0x{request.ReplyHwnd:X}, Results={queryResult.Items.Count}", LogLevel.Debug);

            var responseBuffer = request.IsQuery2
                ? EverythingBinaryResponseBuilder.BuildListV2(
                    queryResult.Items,
                    queryResult.TotalItems,
                    request.Offset,
                    request.RequestFlags,
                    request.SortType,
                    request.IsUnicode)
                : EverythingBinaryResponseBuilder.BuildListV1(
                    queryResult.Items,
                    queryResult.TotalFolders,
                    queryResult.TotalFiles,
                    request.Offset,
                    request.IsUnicode);

            SendReply(request.ReplyHwnd, serverHwnd, request.ReplyCopyDataMessage, responseBuffer);
        }
        catch (Exception ex)
        {
            Logger.Log($"[EverythingIpc] Failed to process query '{request.SearchString}': {ex.Message}", LogLevel.Debug);
        }
    }

    private static void SendReply(IntPtr replyHwnd, IntPtr serverHwnd, uint replyCopyDataMessage, byte[] buffer)
    {
        if (replyHwnd == IntPtr.Zero || buffer.Length == 0) return;

        var pinnedBuffer = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try
        {
            var cds = new COPYDATASTRUCT
            {
                dwData = (IntPtr)replyCopyDataMessage,
                cbData = buffer.Length,
                lpData = pinnedBuffer.AddrOfPinnedObject()
            };
            SendMessage(replyHwnd, EverythingIpcConstants.WM_COPYDATA, serverHwnd, ref cds);
        }
        finally
        {
            pinnedBuffer.Free();
        }
    }

    private IntPtr ProcessRunHistory(EverythingRunHistoryRequest request)
    {
        switch (request.CommandCode)
        {
            case EverythingIpcConstants.CopyDataGetRunCountA:
            case EverythingIpcConstants.CopyDataGetRunCountW:
                var count = _dataProvider.GetRunCount(request.FileName);
                return (IntPtr)count;

            case EverythingIpcConstants.CopyDataSetRunCountA:
            case EverythingIpcConstants.CopyDataSetRunCountW:
                _dataProvider.SetRunCount(request.FileName, request.RunCount);
                return (IntPtr)1;

            case EverythingIpcConstants.CopyDataIncRunCountA:
            case EverythingIpcConstants.CopyDataIncRunCountW:
                var newCount = _dataProvider.IncrementRunCount(request.FileName);
                return (IntPtr)newCount;

            default:
                return IntPtr.Zero;
        }
    }
}
