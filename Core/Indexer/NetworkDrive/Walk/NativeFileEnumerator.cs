using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Lertaro.Core.Indexer.NetworkDrive.Walk;

// Native replacement for Directory.EnumerateFileSystemEntries in TreeBuilder's folder walk.
// .NET 10's FileSystemEnumerator crashes with a CLR error on the exFAT drives this app indexes
// (the crash reproduces with a bare Directory.EnumerateFileSystemEntries recursion, and a plain
// string-concat loop over the same drives does not), so enumeration goes straight to
// FindFirstFileEx/FindNextFile here. The entry returned is deliberately just the NAME plus
// attributes: callers build full paths with plain string concatenation, which has been verified
// safe on the affected drives.
internal static class NativeFileEnumerator
{
    private const int ErrorNoMoreFiles = 18;
    private static readonly IntPtr InvalidHandleValue = new(-1);

    public static IEnumerable<NativeFileEntry> Enumerate(string directoryPath)
    {
        var searchPattern = directoryPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar + "*";

        var handle = FindFirstFileEx(
            searchPattern,
            FindexInfoLevels.FindExInfoBasic,
            out var findData,
            FindexSearchOps.FindExSearchNameMatch,
            IntPtr.Zero,
            0);
        if (handle == InvalidHandleValue)
            throw new Win32Exception(Marshal.GetLastPInvokeError());

        // The first FindFirstFileEx call above is deliberately NOT deferred into the iterator below:
        // TreeBuilder's retry loop catches enumeration failures from the call to Enumerate, matching
        // Directory.EnumerateFileSystemEntries's eager path validation. The iterator only ever runs
        // after a valid handle and first entry have been obtained.
        return EnumerateCore(handle, findData);
    }

    private static IEnumerable<NativeFileEntry> EnumerateCore(IntPtr handle, WIN32_FIND_DATA findData)
    {
        try
        {
            while (true)
            {
                var name = findData.cFileName;
                if (name.Length > 0 && name != "." && name != "..")
                    yield return new NativeFileEntry(name, (FileAttributes)findData.dwFileAttributes);

                if (!FindNextFile(handle, out findData))
                {
                    var error = Marshal.GetLastPInvokeError();
                    if (error != ErrorNoMoreFiles)
                        throw new Win32Exception(error);
                    yield break;
                }
            }
        }
        finally
        {
            FindClose(handle);
        }
    }

    private enum FindexInfoLevels
    {
        FindExInfoBasic = 1
    }

    private enum FindexSearchOps
    {
        FindExSearchNameMatch = 0
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WIN32_FIND_DATA
    {
        public uint dwFileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME ftCreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME ftLastAccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME ftLastWriteTime;
        public uint nFileSizeHigh;
        public uint nFileSizeLow;
        public uint dwReserved0;
        public uint dwReserved1;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string cFileName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)]
        public string cAlternateFileName;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindFirstFileEx(
        string lpFileName,
        FindexInfoLevels fInfoLevelId,
        out WIN32_FIND_DATA lpFindFileData,
        FindexSearchOps fSearchOp,
        IntPtr lpSearchFilter,
        int dwAdditionalFlags);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool FindNextFile(IntPtr hFindFile, out WIN32_FIND_DATA lpFindFileData);

    [DllImport("kernel32.dll")]
    private static extern bool FindClose(IntPtr hFindFile);
}

internal readonly record struct NativeFileEntry(string Name, FileAttributes Attributes)
{
    public bool IsDirectory => (Attributes & FileAttributes.Directory) != 0;
}
