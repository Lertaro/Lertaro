using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Lertaro.Core;

public static class Win32Api
{
    // ==========================================
    // Win32 Constants
    // ==========================================
    public const uint GENERIC_READ = 0x80000000;
    public const uint GENERIC_WRITE = 0x40000000;
    public const uint FILE_SHARE_READ = 1;
    public const uint FILE_SHARE_WRITE = 2;
    public const uint FILE_SHARE_DELETE = 4;
    public const uint OPEN_EXISTING = 3;
    public const IntPtr INVALID_HANDLE_VALUE = -1;
    public const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;

    public const uint FSCTL_QUERY_USN_JOURNAL = 0x000900f4;
    public const uint FSCTL_READ_USN_JOURNAL = 0x000900bb;
    public const uint FSCTL_CREATE_USN_JOURNAL = 0x000900e7;
    public const uint FSCTL_GET_NTFS_VOLUME_DATA = 0x00090064;
    public const uint ERROR_HANDLE_EOF = 38;
    public const int ERROR_INVALID_HANDLE = 6;
    public const int ERROR_NOT_READY = 21;
    public const int ERROR_NO_MORE_FILES = 18;
    public const int ERROR_DEVICE_NOT_CONNECTED = 1167;

    public const uint FILE_FLAG_OPEN_REPARSE_POINT = 0x00200000;
    public const int FileIdExtdDirectoryInfo = 19;

    // USN Reason Codes
    public const uint USN_REASON_DATA_OVERWRITE = 0x00000001;
    public const uint USN_REASON_DATA_EXTEND = 0x00000002;
    public const uint USN_REASON_DATA_TRUNCATION = 0x00000004;
    public const uint USN_REASON_FILE_CREATE = 0x00000100;
    public const uint USN_REASON_FILE_DELETE = 0x00000200;
    public const uint USN_REASON_RENAME_OLD_NAME = 0x00001000;
    public const uint USN_REASON_RENAME_NEW_NAME = 0x00002000;
    public const uint USN_REASON_BASIC_INFO_CHANGE = 0x00008000;
    public const uint USN_REASON_HARD_LINK_CHANGE = 0x00010000;
    public const uint USN_REASON_COMPRESSION_CHANGE = 0x00020000;
    public const uint USN_REASON_ENCRYPTION_CHANGE = 0x00040000;

    // ==========================================
    // Win32 Structures
    // ==========================================
    // Pack = 4 matches the native FILETIME (4-byte-aligned) layout. Without it, the ulong time
    // fields force 8-byte alignment and pad after dwFileAttributes, shifting nFileIndex* to the
    // wrong offsets and yielding a garbage file reference number.
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct BY_HANDLE_FILE_INFORMATION
    {
        public uint dwFileAttributes;
        public ulong ftCreationTime;
        public ulong ftLastAccessTime;
        public ulong ftLastWriteTime;
        public uint dwVolumeSerialNumber;
        public uint nFileSizeHigh;
        public uint nFileSizeLow;
        public uint nNumberOfLinks;
        public uint nFileIndexHigh;
        public uint nFileIndexLow;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct FILE_ID_128
    {
        public ulong Low;
        public ulong High;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct FILE_ID_INFO
    {
        public ulong VolumeSerialNumber;
        public FILE_ID_128 FileId;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct FILE_ID_DESCRIPTOR
    {
        public uint dwSize;
        public uint Type; // 2 = ExtendedFileIdType
        public FILE_ID_128 ExtendedFileId;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct USN_JOURNAL_DATA_V0
    {
        public ulong UsnJournalID;
        public long FirstUsn;
        public long NextUsn;
        public long LowestValidUsn;
        public long MaxUsn;
        public ulong MaximumSize;
        public ulong AllocationDelta;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct READ_USN_JOURNAL_DATA_V0
    {
        public long StartUsn;
        public uint ReasonMask;
        public uint ReturnOnlyOnClose;
        public ulong Timeout;
        public ulong BytesToWaitFor;
        public ulong UsnJournalID;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CREATE_USN_JOURNAL_DATA
    {
        public ulong MaximumSize;
        public ulong AllocationDelta;
    }



    // ==========================================
    // Win32 API Imports
    // ==========================================
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern SafeFileHandle CreateFileW(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile
    );

    // For IOCTLs with no input buffer (e.g. FSCTL_GET_REFS_VOLUME_DATA).
    public const uint FSCTL_GET_REFS_VOLUME_DATA = 0x902D8;

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool DeviceIoControl(
        SafeFileHandle hDevice,
        uint dwIoControlCode,
        ref READ_USN_JOURNAL_DATA_V0 lpInBuffer,
        uint nInBufferSize,
        byte[] lpOutBuffer,
        uint nOutBufferSize,
        out uint lpBytesReturned,
        IntPtr lpOverlapped
    );

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool DeviceIoControl(
        SafeFileHandle hDevice,
        uint dwIoControlCode,
        IntPtr lpInBuffer,
        uint nInBufferSize,
        byte[] lpOutBuffer,
        uint nOutBufferSize,
        out uint lpBytesReturned,
        IntPtr lpOverlapped
    );

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool DeviceIoControl(
        SafeFileHandle hDevice,
        uint dwIoControlCode,
        ref CREATE_USN_JOURNAL_DATA lpInBuffer,
        uint nInBufferSize,
        IntPtr lpOutBuffer,
        uint nOutBufferSize,
        out uint lpBytesReturned,
        IntPtr lpOverlapped
    );

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CloseHandle(IntPtr hObject);

    // Raw volume reads for parsing the $MFT directly.
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool SetFilePointerEx(SafeFileHandle hFile, long liDistanceToMove, out long lpNewFilePointer, uint dwMoveMethod);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool ReadFile(SafeFileHandle hFile, byte[] lpBuffer, uint nNumberOfBytesToRead, out uint lpNumberOfBytesRead, IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool GetVolumeInformationW(
        string lpRootPathName,
        StringBuilder lpVolumeNameBuffer,
        uint nVolumeNameSize,
        out uint lpVolumeSerialNumber,
        out uint lpMaximumComponentLength,
        out uint lpFileSystemFlags,
        StringBuilder lpFileSystemNameBuffer,
        uint nFileSystemNameSize
    );

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool GetFileInformationByHandle(
        SafeFileHandle hFile,
        out BY_HANDLE_FILE_INFORMATION lpFileInformation
    );

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool GetFileInformationByHandleEx(
        SafeFileHandle hFile,
        int FileInformationClass,
        out FILE_ID_INFO lpFileInformation,
        uint dwBufferSize
    );

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool GetFileInformationByHandleEx(
        SafeFileHandle hFile,
        int FileInformationClass,
        IntPtr lpFileInformation,
        uint dwBufferSize
    );

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern SafeFileHandle OpenFileById(
        SafeFileHandle hVolumeFrame,
        ref FILE_ID_DESCRIPTOR lpFileId,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwFlagsAndAttributes
    );

    [DllImport("shell32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr ShellExecuteW(
        IntPtr hwnd,
        string lpOperation,
        string lpFile,
        string lpParameters,
        string lpDirectory,
        int nShowCmd
    );

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool SetProcessWorkingSetSize(IntPtr hProcess, IntPtr dwMinimumWorkingSetSize, IntPtr dwMaximumWorkingSetSize);

    // Non-consuming pipe status check -- unlike PipeStream.IsConnected (a managed flag only updated by
    // an actual Read/Write/Connect call), this queries the OS directly without touching the data stream,
    // so a background watchdog can detect a broken/disconnected pipe while no read or write is in
    // flight (e.g. while a long-running search scan holds the pipe idle on the server side).
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool PeekNamedPipe(SafeHandle hNamedPipe, IntPtr lpBuffer, int nBufferSize, IntPtr lpBytesRead, out int lpTotalBytesAvail, IntPtr lpBytesLeftThisMessage);

    public const int ERROR_BROKEN_PIPE = 109;

    public static void TrimWorkingSet()
    {
        try
        {
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            SetProcessWorkingSetSize(System.Diagnostics.Process.GetCurrentProcess().Handle, -1, -1);
        }
        catch { }
    }

    // ==========================================
    // USN Record Parser using Span
    // ==========================================

}
