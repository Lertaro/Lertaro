using System.Runtime.InteropServices;
using System.Text;

namespace Lertaro.Core.Indexer.Usn;

// Resolves a live file ID when the in-memory index has not observed the object yet. This is kept
// separate from notification matching because the fallback performs real volume I/O.
internal static class UsnFilePathResolver
{
    private const uint ExtendedFileIdType = 2;
    private const int InitialPathCapacity = 512;

    public static bool TryResolve(string drive, UInt128 fileId, out string path)
    {
        path = string.Empty;
        var normalizedDrive = drive.Trim().TrimEnd(':', '\\', '/');
        if (normalizedDrive.Length != 1 || !char.IsLetter(normalizedDrive[0]))
            return false;

        using var volume = Win32Api.CreateFileW(
            $"\\\\.\\{normalizedDrive}:",
            Win32Api.GENERIC_READ,
            Win32Api.FILE_SHARE_READ | Win32Api.FILE_SHARE_WRITE | Win32Api.FILE_SHARE_DELETE,
            IntPtr.Zero,
            Win32Api.OPEN_EXISTING,
            0,
            IntPtr.Zero);
        if (volume.IsInvalid)
            return false;

        var descriptor = new Win32Api.FILE_ID_DESCRIPTOR
        {
            dwSize = (uint)Marshal.SizeOf<Win32Api.FILE_ID_DESCRIPTOR>(),
            Type = ExtendedFileIdType,
            ExtendedFileId = new Win32Api.FILE_ID_128
            {
                Low = (ulong)fileId,
                High = (ulong)(fileId >> 64)
            }
        };
        using var handle = Win32Api.OpenFileById(
            volume,
            ref descriptor,
            0,
            Win32Api.FILE_SHARE_READ | Win32Api.FILE_SHARE_WRITE | Win32Api.FILE_SHARE_DELETE,
            IntPtr.Zero,
            Win32Api.FILE_FLAG_BACKUP_SEMANTICS | Win32Api.FILE_FLAG_OPEN_REPARSE_POINT);
        if (handle.IsInvalid)
            return false;

        var capacity = InitialPathCapacity;
        while (capacity <= 32 * 1024)
        {
            var buffer = new StringBuilder(capacity);
            var length = Win32Api.GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Capacity, 0);
            if (length == 0)
                return false;
            if (length < buffer.Capacity)
            {
                path = NormalizePath(buffer.ToString());
                return path.Length > 0;
            }
            capacity *= 2;
        }

        return false;
    }

    private static string NormalizePath(string path)
    {
        if (path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
            return @"\" + path[7..];
        if (path.StartsWith(@"\\?\", StringComparison.OrdinalIgnoreCase))
            return path[4..];
        return path;
    }
}
