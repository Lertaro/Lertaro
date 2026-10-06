using Lertaro.Core.Services.Installation;
using Microsoft.Win32.SafeHandles;

namespace Lertaro.Core;

/// <summary>
/// Moves persisted settings data from the former product directory without overwriting files created
/// by the current product before settings have first been loaded.
/// </summary>
internal static class SettingsDataDirectoryMigrator
{
    private const string LegacyProductName = "SwiftList";

    public static void Migrate(string currentDirectory, bool updateUserSettings)
    {
        var parentDirectory = Directory.GetParent(currentDirectory)?.FullName;
        if (parentDirectory is null)
            return;

        var legacyDirectory = Path.Combine(parentDirectory, LegacyProductName);
        if (!Directory.Exists(legacyDirectory)) return;
        using var source = DirectoryLockNativeMethods.OpenWithoutFollowing(legacyDirectory, migration: true);
        MergeDirectory(source, currentDirectory);
        if (updateUserSettings) UpdateUserSettings(currentDirectory, legacyDirectory);
    }

    private static void MergeDirectory(SafeFileHandle source, string destinationDirectory)
    {
        // Moving on the same volume retains explicit legacy ACLs. Copy bytes into newly created
        // entries instead, so the destination's user/machine/private-index zone controls access.
        // Leave links in the old tree; never traverse user-selected targets as the service.
        if (((FileAttributes)DirectoryLockNativeMethods.GetInfo(source).dwFileAttributes).HasFlag(FileAttributes.ReparsePoint))
            return;
        Directory.CreateDirectory(destinationDirectory);
        if (File.GetAttributes(destinationDirectory).HasFlag(FileAttributes.ReparsePoint))
            throw new IOException("A settings migration destination cannot be a link.");
        foreach (var name in DirectoryLockNativeMethods.EnumerateNames(source))
        {
            using var child = DirectoryLockNativeMethods.OpenWithoutFollowing(name, source, migration: true);
            var info = DirectoryLockNativeMethods.GetInfo(child);
            var attributes = (FileAttributes)info.dwFileAttributes;
            if (attributes.HasFlag(FileAttributes.ReparsePoint))
                continue;
            if (attributes.HasFlag(FileAttributes.Directory))
            {
                MergeDirectory(child, Path.Combine(destinationDirectory, name));
                continue;
            }
            if (info.nNumberOfLinks > 1) continue;
            var destinationFile = FindAvailablePath(Path.Combine(destinationDirectory, name));
            var temporary = destinationFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    var buffer = new byte[81920];
                    long offset = 0;
                    int count;
                    while ((count = RandomAccess.Read(child, buffer, offset)) != 0)
                    {
                        output.Write(buffer, 0, count);
                        offset += count;
                    }
                    output.Flush(flushToDisk: true);
                }
                File.Move(temporary, destinationFile);
                DirectoryLockNativeMethods.Delete(child);
            }
            finally { File.Delete(temporary); }
        }
        if (!DirectoryLockNativeMethods.EnumerateNames(source).Any())
            DirectoryLockNativeMethods.Delete(source);
    }

    private static string FindAvailablePath(string path)
    {
        if (!File.Exists(path))
            return path;

        var extension = Path.GetExtension(path);
        var basePath = extension.Length == 0 ? path : path[..^extension.Length];
        for (var suffix = 1; ; suffix++)
        {
            var candidate = $"{basePath}.legacy-{suffix}{extension}";
            if (!File.Exists(candidate))
                return candidate;
        }
    }

    private static void UpdateUserSettings(string directory, string legacyDirectory)
    {
        var settingsPath = Path.Combine(directory, "user-settings.json");
        if (!File.Exists(settingsPath))
            return;

        var json = File.ReadAllText(settingsPath);
        // Rewrite only the legacy data-directory path, in its raw and JSON string-escaped backslash
        // forms, instead of the bare product name: a blind word replace corrupts legitimate user
        // strings that merely contain "SwiftList" (favorite names, plugin titles, ...). ponytail:
        // forward-slash or \u005c escape variants of the legacy path are not rewritten -- the app only
        // ever wrote backslash paths, so nothing in a settings file it produced needs them.
        var updatedJson = json
            .Replace(legacyDirectory.Replace("\\", "\\\\"), directory.Replace("\\", "\\\\"), StringComparison.Ordinal)
            .Replace(legacyDirectory, directory, StringComparison.Ordinal);
        if (updatedJson != json)
            AtomicFileStore.Write(settingsPath, updatedJson);
    }
}
