using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Lertaro.Core;

public static class VolumeHelper
{
    public readonly record struct VolumeIdentity(string FileSystemType, uint SerialNumber);

    public static string GetVolumeCacheKey(VolumeIdentity identity)
    {
        var raw = $"{identity.SerialNumber:x8}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static string? GetVolumeId(string driveLetter)
    {
        var identity = GetVolumeIdentity(driveLetter);
        return identity.HasValue ? GetVolumeCacheKey(identity.Value) : null;
    }

    public static UInt128? GetRootFrn(string driveLetter)
    {
        var path = $"{driveLetter}:\\";
        using var handle = Win32Api.CreateFileW(
            path,
            0,
            Win32Api.FILE_SHARE_READ | Win32Api.FILE_SHARE_WRITE,
            IntPtr.Zero,
            Win32Api.OPEN_EXISTING,
            Win32Api.FILE_FLAG_BACKUP_SEMANTICS,
            IntPtr.Zero
        );

        if (handle.IsInvalid)
            return null;

        try
        {
            if (Win32Api.GetFileInformationByHandleEx(handle, 18, out var info, (uint)Marshal.SizeOf<Win32Api.FILE_ID_INFO>()))
            {
                return new UInt128(info.FileId.High, info.FileId.Low);
            }
        }
        catch
        {
            // Fall back
        }

        if (Win32Api.GetFileInformationByHandle(handle, out var stdInfo))
        {
            var frn = ((ulong)stdInfo.nFileIndexHigh << 32) | stdInfo.nFileIndexLow;
            return frn;
        }

        return null;
    }

    public static List<string> DetectIndexableLocalDrives() => DriveInfo.GetDrives()
        .Where(d => d.IsReady && d.DriveType != DriveType.Network && d.Name.Length >= 2)
        .Select(d => d.Name.Split(':')[0].ToUpperInvariant())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(d => d, StringComparer.OrdinalIgnoreCase)
        .ToList();

    public static string GetFileSystemType(string driveLetter)
    {
        var identity = GetVolumeIdentity(driveLetter);
        return identity?.FileSystemType ?? "NTFS";
    }

    public static VolumeIdentity? GetVolumeIdentity(string driveLetter)
    {
        var rootPath = $"{driveLetter}:\\";
        var volumeName = new StringBuilder(260);
        var fileSystemName = new StringBuilder(260);
        var success = Win32Api.GetVolumeInformationW(
            rootPath,
            volumeName, (uint)volumeName.Capacity,
            out var serial, out _, out _,
            fileSystemName, (uint)fileSystemName.Capacity
        );

        return success ? new VolumeIdentity(fileSystemName.ToString(), serial) : null;
    }

    public static string GetDisplayFileSystemType(string driveLetter)
        => GetFileSystemType(driveLetter);

    // Whether `driveLetter` sits on a filesystem this app's USN-journal indexing pipeline supports (NTFS
    // or ReFS) -- the single source of truth for this check, previously duplicated identically across
    // DriveMonitorFactory, DriveRecovery, SearchEngineInitializer, IndexBuilder, and JournalReader.
    public static bool SupportsUsnJournal(string driveLetter) => IsJournalCapableFileSystem(GetFileSystemType(driveLetter));

    // For callers that already have a resolved filesystem-type string in hand (avoids a redundant
    // GetFileSystemType round trip).
    public static bool IsJournalCapableFileSystem(string fileSystemType) =>
        fileSystemType.Equals("NTFS", StringComparison.OrdinalIgnoreCase) || fileSystemType.Equals("ReFS", StringComparison.OrdinalIgnoreCase);
}
