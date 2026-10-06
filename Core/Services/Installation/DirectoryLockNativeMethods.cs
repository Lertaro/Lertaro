using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Lertaro.Core.Services.Installation;

// Win32 interop for InstallDirectoryLock, kept apart so that file holds only the policy. Everything here
// works on an already-open handle, which is the point: see InstallDirectoryLock's remarks.
internal static partial class DirectoryLockNativeMethods
{
    private const uint DeleteAccess = 0x00010000, ReadControl = 0x00020000, WriteDac = 0x00040000, WriteOwner = 0x00080000;
    private const uint FileReadAttributes = 0x0080;
    private const uint OwnerSecurityInformation = 0x1, DaclSecurityInformation = 0x4;
    private const uint ProtectedDaclSecurityInformation = 0x80000000, UnprotectedDaclSecurityInformation = 0x20000000;
    private const int ErrorSharingViolation = 32;
    private const int FileDispositionInfo = 4;

    /// <summary>
    /// Opens <paramref name="path"/> itself, never what a reparse point there refers to. Asks for DELETE so a
    /// link can be removed through the same handle; a file another process holds open without sharing
    /// delete is opened without it (such a file is in use by its owner, not a planted link).
    /// </summary>
    public static SafeFileHandle OpenWithoutFollowing(string path, SafeFileHandle? parent = null, bool migration = false)
    {
        const uint flags = Win32Api.FILE_FLAG_BACKUP_SEMANTICS | Win32Api.FILE_FLAG_OPEN_REPARSE_POINT;
        var share = migration ? Win32Api.FILE_SHARE_READ : Win32Api.FILE_SHARE_READ | Win32Api.FILE_SHARE_WRITE | Win32Api.FILE_SHARE_DELETE;
        var access = ReadControl | FileReadAttributes | 0x00100001u; // SYNCHRONIZE + READ_DATA/LIST_DIRECTORY
        if (!migration) access |= WriteDac | WriteOwner;

        if (parent != null)
        {
            var relative = OpenRelative(parent, path, access | DeleteAccess, share, out var errorCode);
            if (relative.IsInvalid && errorCode == ErrorSharingViolation && !migration)
            {
                relative.Dispose();
                relative = OpenRelative(parent, path, access, share, out errorCode);
            }
            if (!relative.IsInvalid) return relative;
            relative.Dispose();
            throw new IOException($"Could not open child '{path}'.", new Win32Exception(errorCode));
        }

        var handle = Win32Api.CreateFileW(path, access | DeleteAccess, share, IntPtr.Zero, Win32Api.OPEN_EXISTING, flags, IntPtr.Zero);
        if (handle.IsInvalid && Marshal.GetLastWin32Error() == ErrorSharingViolation && !migration)
        {
            handle.Dispose();
            handle = Win32Api.CreateFileW(path, access, share, IntPtr.Zero, Win32Api.OPEN_EXISTING, flags, IntPtr.Zero);
        }

        if (!handle.IsInvalid)
            return handle;

        var error = Marshal.GetLastWin32Error();
        handle.Dispose();
        throw new IOException($"Could not open '{path}'.", new Win32Exception(error));
    }

    // NtOpenFile's RootDirectory pins the actual parent object. A rename/junction swap after enumeration
    // cannot redirect this open. Layouts below follow the Windows SDK winternl.h declarations.
    private static unsafe SafeFileHandle OpenRelative(SafeFileHandle parent, string name, uint access, uint share, out int error)
    {
        if (name != Path.GetFileName(name) || name is "." or "..") throw new ArgumentException("Expected one file name.");
        fixed (char* chars = name)
        {
            var unicode = new UnicodeName { Length = checked((ushort)(name.Length * 2)), MaximumLength = checked((ushort)(name.Length * 2)), Buffer = chars };
            var added = false;
            try
            {
                parent.DangerousAddRef(ref added);
                var attributes = new ObjectAttributes
                {
                    Length = (uint)sizeof(ObjectAttributes), RootDirectory = parent.DangerousGetHandle(),
                    ObjectName = &unicode, Attributes = 0x40 // OBJ_CASE_INSENSITIVE
                };
                // FILE_SYNCHRONOUS_IO_NONALERT | FILE_OPEN_FOR_BACKUP_INTENT | FILE_OPEN_REPARSE_POINT
                var status = NtOpenFile(out var handle, access, &attributes, out _, share, 0x00204020);
                error = status < 0 ? (int)RtlNtStatusToDosError(status) : 0;
                return handle;
            }
            finally { if (added) parent.DangerousRelease(); }
        }
    }

    public static IEnumerable<string> EnumerateNames(SafeFileHandle directory)
    {
        var buffer = new byte[16 * 1024];
        var infoClass = 15; // FileFullDirectoryRestartInfo, then FileFullDirectoryInfo
        while (true)
        {
            if (GetFileInformationByHandleEx(directory, infoClass, buffer, (uint)buffer.Length) == 0)
            {
                var error = Marshal.GetLastPInvokeError();
                if (error == 18) yield break; // ERROR_NO_MORE_FILES
                throw new IOException("Could not enumerate the open directory.", new Win32Exception(error));
            }
            infoClass = 14;
            var offset = 0;
            while (true)
            {
                // FILE_FULL_DIR_INFO: FileNameLength at 60, WCHAR FileName[] at 68.
                var next = BitConverter.ToInt32(buffer, offset);
                var length = BitConverter.ToInt32(buffer, offset + 60);
                if (length < 0 || length % 2 != 0 || length > buffer.Length - offset - 68)
                    throw new IOException("Invalid directory entry length.");
                var name = Encoding.Unicode.GetString(buffer, offset + 68, length);
                if (name is not "." and not "..") yield return name;
                if (next == 0) break;
                if (next < 68 || next > buffer.Length - offset - 68) throw new IOException("Invalid directory entry offset.");
                offset += next;
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct UnicodeName { public ushort Length, MaximumLength; public char* Buffer; }
    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct ObjectAttributes
    {
        public uint Length; public IntPtr RootDirectory; public UnicodeName* ObjectName;
        public uint Attributes; public IntPtr SecurityDescriptor, SecurityQualityOfService;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct IoStatusBlock { public IntPtr Status; public nuint Information; }

    [LibraryImport("ntdll.dll")]
    private static unsafe partial int NtOpenFile(out SafeFileHandle file, uint access, ObjectAttributes* attributes,
        out IoStatusBlock status, uint share, uint options);
    [LibraryImport("ntdll.dll")]
    private static partial uint RtlNtStatusToDosError(int status);
    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial int GetFileInformationByHandleEx(SafeFileHandle file, int infoClass, [Out] byte[] buffer, uint size);

    public static Win32Api.BY_HANDLE_FILE_INFORMATION GetInfo(SafeFileHandle handle) =>
        Win32Api.GetFileInformationByHandle(handle, out var info) ? info : throw new Win32Exception();

    public static void SetSecurity(SafeFileHandle handle, RawSecurityDescriptor descriptor, bool isProtected)
    {
        var bytes = new byte[descriptor.BinaryLength];
        descriptor.GetBinaryForm(bytes, 0);
        var information = OwnerSecurityInformation | DaclSecurityInformation |
            (isProtected ? ProtectedDaclSecurityInformation : UnprotectedDaclSecurityInformation);
        if (!SetKernelObjectSecurity(handle, information, bytes))
            throw new UnauthorizedAccessException("Could not set the owner and permissions.", new Win32Exception());
    }

    public static void Delete(SafeFileHandle handle)
    {
        byte deleteFile = 1;
        if (!SetFileInformationByHandle(handle, FileDispositionInfo, ref deleteFile, 1))
            throw new IOException("Could not remove the link.", new Win32Exception());
    }

    /// <summary>
    /// Enables the named privileges on this process's token and returns the token plus the previous state
    /// to restore. Privileges the token does not hold are skipped (AdjustTokenPrivileges reports
    /// ERROR_NOT_ALL_ASSIGNED and changes the rest). Null when the token cannot be opened at all.
    /// </summary>
    public static SafeAccessTokenHandle? EnablePrivileges(string[] names, out byte[]? previous)
    {
        const uint adjustPrivileges = 0x0020, query = 0x0008, enabled = 0x0002;
        previous = null;
        if (!OpenProcessToken(Win32Api.GetCurrentProcess(), adjustPrivileges | query, out var token))
            return null;

        // TOKEN_PRIVILEGES: a count, then one LUID_AND_ATTRIBUTES (8-byte LUID, 4-byte attributes) per entry.
        var state = new byte[4 + (12 * names.Length)];
        BitConverter.TryWriteBytes(state, names.Length);
        for (var i = 0; i < names.Length; i++)
        {
            if (!LookupPrivilegeValue(null, names[i], out var luid))
                continue;
            BitConverter.TryWriteBytes(state.AsSpan(4 + (12 * i)), luid);
            BitConverter.TryWriteBytes(state.AsSpan(12 + (12 * i)), enabled);
        }

        var buffer = new byte[state.Length];
        if (AdjustTokenPrivileges(token, false, state, buffer.Length, buffer, out _))
            previous = buffer;
        return token;
    }

    public static void RestorePrivileges(SafeAccessTokenHandle token, byte[] previous) =>
        AdjustTokenPrivileges(token, false, previous, 0, null, out _);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool SetKernelObjectSecurity(SafeFileHandle handle, uint securityInformation, byte[] securityDescriptor);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetFileInformationByHandle(SafeFileHandle handle, int fileInformationClass, ref byte information, uint size);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out SafeAccessTokenHandle tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool LookupPrivilegeValue(string? systemName, string name, out long luid);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool AdjustTokenPrivileges(SafeAccessTokenHandle token, bool disableAll, byte[] newState,
        int bufferLength, byte[]? previousState, out int returnLength);
}
