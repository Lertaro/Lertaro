using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Lertaro.Core.IndexV2;
using Lertaro.Core.IndexV2.Delta;

namespace Lertaro.Core.Indexer.Usn;

// Keeps handle-bound metadata I/O separate from delta routing and below the per-file line limit.
internal static class UsnMetadataReader
{
    internal readonly record struct Metadata(long Size, uint Created, uint Modified, uint Accessed, FileRecordFlags Flags);

    internal static Metadata? Read(string path, UInt128 expectedId)
    {
        // A trailing separator would follow a directory symlink despite OPEN_REPARSE_POINT.
        using var handle = Win32Api.CreateFileW(Path.TrimEndingDirectorySeparator(path), 0,
            Win32Api.FILE_SHARE_READ | Win32Api.FILE_SHARE_WRITE | Win32Api.FILE_SHARE_DELETE, IntPtr.Zero,
            Win32Api.OPEN_EXISTING, Win32Api.FILE_FLAG_BACKUP_SEMANTICS | Win32Api.FILE_FLAG_OPEN_REPARSE_POINT, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            if (error is 2 or 3) return null;
            throw new Win32Exception(error);
        }
        if (!Win32Api.GetFileInformationByHandleEx(handle, 18, out var id, 24))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        if (new UInt128(id.FileId.High, id.FileId.Low) != expectedId) return null;
        var basic = new byte[40]; var standard = new byte[24];
        if (!GetFileInformationByHandleEx(handle, 0, basic, 40) || !GetFileInformationByHandleEx(handle, 1, standard, 24))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        return Decode(basic, standard);
    }

    internal static Metadata Decode(ReadOnlySpan<byte> basic, ReadOnlySpan<byte> standard)
    {
        if (basic.Length < 40 || standard.Length < 24) throw new InvalidDataException("Truncated file metadata.");
        var attributes = (FileAttributes)BinaryPrimitives.ReadUInt32LittleEndian(basic[32..]);
        var size = attributes.HasFlag(FileAttributes.Directory) ? 0 : BinaryPrimitives.ReadInt64LittleEndian(standard[8..]);
        if (size < 0) throw new InvalidDataException("Negative logical file size.");
        return new(size,
            FileTimeHelper.FileTimeToUnixSeconds(BinaryPrimitives.ReadInt64LittleEndian(basic)),
            FileTimeHelper.FileTimeToUnixSeconds(BinaryPrimitives.ReadInt64LittleEndian(basic[16..])),
            FileTimeHelper.FileTimeToUnixSeconds(BinaryPrimitives.ReadInt64LittleEndian(basic[8..])),
            FileRecordFlagsHelper.FromAttributes(attributes));
    }

    internal static void Refresh(LiveIndex live, HashSet<UInt128> ids)
    {
        var paths = live.Read((snapshot, delta) =>
        {
            var found = new Dictionary<UInt128, string>();
            foreach (var id in ids)
                if (delta.TryGetPathForFrn(id, out var path)) found.Add(id, path);
            return found;
        });
        var values = new Dictionary<UInt128, Metadata>();
        foreach (var (id, path) in paths)
            if (Read(path, id) is { } metadata) values.Add(id, metadata);
        live.Mutate((snapshot, delta) =>
        {
            foreach (var (id, metadata) in values)
            {
                DeltaLinkOps.UpdateMetadata(delta, id, metadata.Size, metadata.Created, metadata.Modified, metadata.Accessed);
                DeltaLinkOps.UpdateFlags(delta, id, metadata.Flags);
            }
        });
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(SafeFileHandle handle, int kind, byte[] buffer, uint length);
}
