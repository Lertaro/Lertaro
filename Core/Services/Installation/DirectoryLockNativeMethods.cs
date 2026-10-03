using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using Microsoft.Win32.SafeHandles;

namespace Lertaro.Core.Services.Installation;

// Win32 interop for InstallDirectoryLock, kept apart so that file holds only the policy. Everything here
// works on an already-open handle, which is the point: see InstallDirectoryLock's remarks.
internal static class DirectoryLockNativeMethods
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
    public static SafeFileHandle OpenWithoutFollowing(string path)
    {
        const uint flags = Win32Api.FILE_FLAG_BACKUP_SEMANTICS | Win32Api.FILE_FLAG_OPEN_REPARSE_POINT;
        const uint share = Win32Api.FILE_SHARE_READ | Win32Api.FILE_SHARE_WRITE | Win32Api.FILE_SHARE_DELETE;
        const uint access = ReadControl | WriteDac | WriteOwner | FileReadAttributes;

        var handle = Win32Api.CreateFileW(path, access | DeleteAccess, share, IntPtr.Zero, Win32Api.OPEN_EXISTING, flags, IntPtr.Zero);
        if (handle.IsInvalid && Marshal.GetLastWin32Error() == ErrorSharingViolation)
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
