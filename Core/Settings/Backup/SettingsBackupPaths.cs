using Lertaro.Core.Services.Installation;
using Microsoft.Win32.SafeHandles;

namespace Lertaro.Core;

// Pins every ancestor, including those above UserData. A check-then-open of a full path alone would
// reintroduce the junction/rename race repaired in 58e7ae14. The chain down to Root may pass through
// links -- a portable copy installed by scoop is reached through its `current` junction and keeps its
// data in a persisted `Data` junction under it -- because every component is held open below and
// FILE_SHARE_DELETE is not granted, so none of them can be renamed or retargeted afterwards. Entries
// inside Root are still opened link-free: those are the ones another local user could plant.
internal sealed class SettingsBackupPaths : IDisposable
{
    private readonly Dictionary<string, SafeFileHandle> _directories = new(StringComparer.OrdinalIgnoreCase);
    public string Root { get; }

    public SettingsBackupPaths(string root)
    {
        Root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        try { OpenDirectory(Root); }
        catch { Dispose(); throw; }
    }

    public static string Validate(string path)
    {
        if (string.IsNullOrEmpty(path) || path.Length > 1024 || path.Contains('\\') || path.Contains('\ufffd') || Path.IsPathRooted(path))
            throw new InvalidDataException($"Invalid backup path: {path}");
        foreach (var part in path.Split('/'))
        {
            var stem = part.Split('.')[0];
            if (part.Length == 0 || part is "." or ".." || part.EndsWith(' ') || part.EndsWith('.') ||
                part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                new[] { "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$" }.Contains(stem, StringComparer.OrdinalIgnoreCase) ||
                (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) &&
                 "123456789¹²³".Contains(stem[3])))
                throw new InvalidDataException($"Invalid backup path: {path}");
        }
        return path;
    }

    public string Resolve(string relative) => Path.Combine(Root, Validate(relative).Replace('/', Path.DirectorySeparatorChar));

    private SafeFileHandle OpenDirectory(string path, bool create = false)
    {
        if (_directories.TryGetValue(path, out var existing)) return existing;
        var parent = Directory.GetParent(path)?.FullName;
        var parentHandle = parent == null ? null : OpenDirectory(parent);
        if (create && parentHandle != null) Directory.CreateDirectory(path);
        var handle = DirectoryLockNativeMethods.OpenForBackup(parent == null ? path : Path.GetFileName(path), parentHandle, true,
            pathToRoot: IsPathToRoot(path));
        _directories.Add(path, handle);
        return handle;
    }

    // Root itself and every component above it: the filesystem's own layout, not content this tree
    // validates. Everything below Root answers false, so a link there is still refused.
    private bool IsPathToRoot(string path)
    {
        if (path.Equals(Root, StringComparison.OrdinalIgnoreCase)) return true;
        var prefix = path.EndsWith(Path.DirectorySeparatorChar) ? path : path + Path.DirectorySeparatorChar;
        return Root.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    public void CreateParents(string relative)
    {
        var path = Root;
        foreach (var part in Validate(relative).Split('/').SkipLast(1))
        {
            path = Path.Combine(path, part);
            OpenDirectory(path, create: true);
        }
    }

    public FileStream Read(string relative)
    {
        var path = Resolve(relative);
        var parent = OpenDirectory(Path.GetDirectoryName(path)!);
        var handle = DirectoryLockNativeMethods.OpenForBackup(Path.GetFileName(path), parent, false);
        return new FileStream(handle, FileAccess.Read);
    }

    public IEnumerable<string> Files(string relativeDirectory)
    {
        var path = Resolve(relativeDirectory);
        SafeFileHandle handle;
        try { handle = OpenDirectory(path); }
        catch (FileNotFoundException) { yield break; }
        foreach (var name in DirectoryLockNativeMethods.EnumerateNames(handle).Order(StringComparer.Ordinal))
        {
            var relative = Validate(relativeDirectory + "/" + name);
            // Ancestors stay pinned while the leaf is inspected and subsequently opened without following.
            var attributes = File.GetAttributes(Resolve(relative));
            if (attributes.HasFlag(FileAttributes.ReparsePoint)) throw new IOException($"Backup path is a link: {relative}");
            if (attributes.HasFlag(FileAttributes.Directory))
            {
                foreach (var child in Files(relative)) yield return child;
            }
            else yield return relative;
        }
    }

    public IEnumerable<string> Directories(string relativeDirectory)
    {
        SafeFileHandle handle;
        try { handle = OpenDirectory(Resolve(relativeDirectory)); }
        catch (FileNotFoundException) { yield break; }
        foreach (var name in DirectoryLockNativeMethods.EnumerateNames(handle).Order(StringComparer.Ordinal))
        {
            var relative = Validate(relativeDirectory + "/" + name);
            if (!File.GetAttributes(Resolve(relative)).HasFlag(FileAttributes.Directory)) continue;
            OpenDirectory(Resolve(relative));
            yield return relative;
        }
    }

    public void Write(string relative, Action<Stream> write)
    {
        CreateParents(relative);
        var target = Resolve(relative);
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            {
                write(output);
                output.Flush(true);
            }
            // Move the new inode, not source metadata or the old target's security descriptor.
            // Replacing a hard link changes that directory entry only; it never writes its referent.
            File.Move(temporary, target, overwrite: true);
        }
        finally { File.Delete(temporary); }
    }

    public void ClearFiles(string relativeDirectory)
    {
        // Do not recursively delete by a path after closing its handles. Remove leaf entries while
        // ancestors remain pinned; leaving empty staging directories costs no retained payload data.
        foreach (var file in Files(relativeDirectory).ToList()) File.Delete(Resolve(file));
    }

    public void Dispose()
    {
        foreach (var handle in _directories.Values.Reverse()) handle.Dispose();
        _directories.Clear();
    }
}
