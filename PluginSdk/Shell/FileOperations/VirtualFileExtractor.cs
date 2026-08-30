using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using IComDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;

namespace Lertaro.PluginSdk.Shell.FileOperations;

/// <summary>
/// Writes out the files a drag is carrying that do not exist on disk yet.
/// </summary>
/// <remarks>
/// An image dragged out of a browser, an attachment dragged out of Outlook, a file dragged out of a zip
/// preview: none of them are a path, so none of them arrive as CF_HDROP. What arrives instead is a pair
/// of formats -- FileGroupDescriptorW naming the files, and FileContents carrying the bytes of one of
/// them per call, chosen by an index. That index is the whole problem: WPF's IDataObject has no way to
/// pass it, so the only route is the COM interface underneath, which is what all of this is.
///
/// Deliberately not filtered by type. Deciding whether something "is an image" means trusting an
/// extension or sniffing bytes, and a panel whose whole purpose is to put things into a folder has no
/// reason to refuse one kind of thing that a drag was willing to hand over.
/// </remarks>
public static class VirtualFileExtractor
{
    private static readonly int CfFileGroupDescriptorW = DataFormatId("FileGroupDescriptorW");
    private static readonly int CfFileContents = DataFormatId("FileContents");

    private const uint FILE_ATTRIBUTE_DIRECTORY = 0x10;
    private const int MaxPath = 260;

    /// <summary>Whether this drag carries files that have to be written out rather than copied.</summary>
    public static bool HasVirtualFiles(System.Windows.IDataObject? data)
        => data != null && data.GetDataPresent("FileGroupDescriptorW");

    /// <summary>
    /// Writes every file the drag describes into <paramref name="targetFolder"/> and returns what was
    /// actually written.
    /// </summary>
    /// <remarks>
    /// One file failing costs that file only: a drag of six images where the source cannot produce the
    /// third should still land the other five, which is what a partial result means here.
    /// </remarks>
    public static List<string> Extract(System.Windows.IDataObject? data, string targetFolder)
    {
        var written = new List<string>();
        if (data is not IComDataObject com || string.IsNullOrEmpty(targetFolder))
            return written;

        var names = ReadDescriptors(com);
        for (var i = 0; i < names.Count; i++)
        {
            if (names[i] == null) continue;

            try
            {
                var path = ResolveDestination(targetFolder, names[i]!);
                if (path == null)
                {
                    Logger.Log($"[VirtualFileExtractor] Refusing '{names[i]}': it resolves outside the target folder.", LogLevel.Error);
                    continue;
                }

                var parent = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);

                if (WriteContents(com, i, path))
                    written.Add(path);
            }
            catch (Exception ex)
            {
                Logger.Log($"[VirtualFileExtractor] '{names[i]}' could not be written: {ex.Message}", LogLevel.Error);
            }
        }

        return written;
    }

    /// <summary>The names in the group descriptor. Null for an entry that is not a file to write.</summary>
    private static List<string?> ReadDescriptors(IComDataObject com)
    {
        var names = new List<string?>();
        var format = new FORMATETC
        {
            cfFormat = (short)CfFileGroupDescriptorW,
            dwAspect = DVASPECT.DVASPECT_CONTENT,
            lindex = -1,
            tymed = TYMED.TYMED_HGLOBAL,
        };

        STGMEDIUM medium = default;
        try
        {
            com.GetData(ref format, out medium);
            if (medium.tymed != TYMED.TYMED_HGLOBAL || medium.unionmember == IntPtr.Zero)
                return names;

            var block = GlobalLock(medium.unionmember);
            if (block == IntPtr.Zero) return names;

            try
            {
                var blockSize = (long)GlobalSize(medium.unionmember);
                var count = Marshal.ReadInt32(block);
                // The count is followed by that many fixed-size descriptors, so each one is found by
                // walking rather than by any pointer in the block. Clamp against the actual HGLOBAL size:
                // a hostile/truncated descriptor must not make us read past the block.
                var descriptorSize = Marshal.SizeOf<FileDescriptorW>();
                if (count < 0 || (long)count > (blockSize - sizeof(int)) / descriptorSize)
                {
                    Logger.Log($"[VirtualFileExtractor] Invalid file group descriptor count: {count}.", LogLevel.Error);
                    return names;
                }
                for (var i = 0; i < count; i++)
                {
                    var at = IntPtr.Add(block, sizeof(int) + (i * descriptorSize));
                    var descriptor = Marshal.PtrToStructure<FileDescriptorW>(at);

                    // A virtual folder has no contents stream to ask for. Skipping it is what leaves the
                    // files inside it (which arrive as their own entries, named with the folder in front)
                    // to be written on their own.
                    names.Add((descriptor.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) != 0
                        ? null
                        : descriptor.cFileName);
                }
            }
            finally
            {
                GlobalUnlock(medium.unionmember);
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"[VirtualFileExtractor] Could not read the file group descriptor: {ex.Message}", LogLevel.Error);
        }
        finally
        {
            ReleaseStgMedium(ref medium);
        }

        return names;
    }

    /// <summary>Asks for one file's bytes by index and writes them. False if the source had none.</summary>
    private static bool WriteContents(IComDataObject com, int index, string destination)
    {
        var format = new FORMATETC
        {
            cfFormat = (short)CfFileContents,
            dwAspect = DVASPECT.DVASPECT_CONTENT,
            // The index IS the request. Everything else about this format is the same for every file in
            // the drag, which is why WPF's own IDataObject cannot express it.
            lindex = index,
            tymed = TYMED.TYMED_ISTREAM | TYMED.TYMED_HGLOBAL,
        };

        STGMEDIUM medium = default;
        try
        {
            com.GetData(ref format, out medium);

            return medium.tymed switch
            {
                TYMED.TYMED_ISTREAM => WriteStream(medium, destination),
                TYMED.TYMED_HGLOBAL => WriteGlobal(medium, destination),
                _ => false,
            };
        }
        finally
        {
            ReleaseStgMedium(ref medium);
        }
    }

    private static bool WriteStream(STGMEDIUM medium, string destination)
    {
        if (medium.unionmember == IntPtr.Zero) return false;

        var stream = (IStream)Marshal.GetObjectForIUnknown(medium.unionmember);
        try
        {
            using var file = File.Create(destination);
            var buffer = new byte[81920];
            var readCount = Marshal.AllocCoTaskMem(sizeof(int));
            try
            {
                while (true)
                {
                    stream.Read(buffer, buffer.Length, readCount);
                    var read = Marshal.ReadInt32(readCount);
                    if (read <= 0) break;
                    file.Write(buffer, 0, read);
                }
            }
            finally
            {
                Marshal.FreeCoTaskMem(readCount);
            }
            return true;
        }
        finally
        {
            Marshal.ReleaseComObject(stream);
        }
    }

    private static bool WriteGlobal(STGMEDIUM medium, string destination)
    {
        if (medium.unionmember == IntPtr.Zero) return false;

        var block = GlobalLock(medium.unionmember);
        if (block == IntPtr.Zero) return false;

        try
        {
            var size = (int)GlobalSize(medium.unionmember);
            if (size <= 0) return false;

            var bytes = new byte[size];
            Marshal.Copy(block, bytes, 0, size);
            File.WriteAllBytes(destination, bytes);
            return true;
        }
        finally
        {
            GlobalUnlock(medium.unionmember);
        }
    }

    /// <summary>
    /// Where a name from a drag is allowed to land under a folder, or null if it is not allowed to land
    /// anywhere. Creates nothing -- the caller makes the directory once it has an answer.
    /// </summary>
    /// <remarks>
    /// The name comes from another process and is not to be trusted with a path. A descriptor may
    /// legitimately carry a relative one ("images\photo.png", when a folder was dragged), so separators
    /// cannot simply be rejected: the name is resolved and the result checked to be genuinely inside the
    /// target, which is what stops "..\..\somewhere" from being written outside it. An absolute name is
    /// refused by the same check, since Path.Combine lets one replace the root entirely.
    ///
    /// Public because it is the whole of a decision worth making the same way everywhere, and any other
    /// caller writing out data a drag handed it faces exactly this question.
    /// </remarks>
    public static string? ResolveDestination(string targetFolder, string name)
    {
        if (string.IsNullOrWhiteSpace(targetFolder) || string.IsNullOrWhiteSpace(name)) return null;

        string root, combined;
        try
        {
            root = Path.GetFullPath(targetFolder);
            combined = Path.GetFullPath(Path.Combine(root, name));
        }
        catch
        {
            // An unusable name (illegal characters, a device name, longer than the OS allows) is refused
            // rather than thrown over: one bad descriptor in a drag must not cost the rest.
            return null;
        }

        var prefix = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return combined.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? combined : null;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct FileDescriptorW
    {
        public uint dwFlags;
        public Guid clsid;
        public int sizelCx;
        public int sizelCy;
        public int pointlX;
        public int pointlY;
        public uint dwFileAttributes;
        public FILETIME ftCreationTime;
        public FILETIME ftLastAccessTime;
        public FILETIME ftLastWriteTime;
        public uint nFileSizeHigh;
        public uint nFileSizeLow;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = MaxPath)]
        public string cFileName;
    }

    private static int DataFormatId(string name) => System.Windows.DataFormats.GetDataFormat(name).Id;

    [DllImport("kernel32.dll")] private static extern IntPtr GlobalLock(IntPtr handle);
    [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(IntPtr handle);
    [DllImport("kernel32.dll")] private static extern UIntPtr GlobalSize(IntPtr handle);
    [DllImport("ole32.dll")] private static extern void ReleaseStgMedium(ref STGMEDIUM medium);
}
