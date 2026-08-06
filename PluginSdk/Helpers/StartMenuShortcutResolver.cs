using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace Lertaro.PluginSdk.Helpers;

/// <summary>
/// Resolves .lnk shortcut targets and enumerates start menu directories.
/// </summary>
public static class StartMenuShortcutResolver
{
    private const int MAX_PATH = 260;
    private const uint SLGP_UNCPRIORITY = 0x0002;

    private static readonly HashSet<string> AppFileExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".lnk",
        ".appref-ms",
        ".url",
        ".exe"
    };

    /// <summary>
    /// The same extensions <see cref="ShouldIndex"/> accepts, as a Win32 filter pattern for the host's
    /// own directory enumeration (see <c>DirectoryIndexerService.EnumerateDirectoryAsync</c>).
    /// </summary>
    /// <remarks>
    /// Built from the one list rather than written out a second time: a pattern that drifted from
    /// <see cref="ShouldIndex"/> would either hide an app kind or drag files back that are then dropped
    /// anyway, and nothing would say which of the two had happened.
    /// </remarks>
    public static string AppFilePattern { get; } = string.Join(';', AppFileExtensions.Select(ext => "*" + ext));

    public static bool ShouldIndex(string path)
    {
        var fileName = Path.GetFileName(path);
        if (fileName.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase))
            return false;

        return AppFileExtensions.Contains(Path.GetExtension(path));
    }

    public static IEnumerable<string> GetStartMenuRoots()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders");
            if (key != null)
            {
                var commonStartMenu = key.GetValue("Common Start Menu") as string;
                if (!string.IsNullOrEmpty(commonStartMenu))
                {
                    AddIfDirectory(roots, Environment.ExpandEnvironmentVariables(commonStartMenu));
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"[StartMenuShortcutResolver] Failed to read common start menu from registry: {ex.Message}", LogLevel.Warn);
        }

        if (roots.Count == 0)
        {
            var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            AddIfDirectory(roots, Path.Combine(programData, "Microsoft", "Windows", "Start Menu"));
        }

        foreach (var userDir in UserProfileHelper.GetAllUserProfilePaths())
        {
            AddIfDirectory(roots, UserProfileHelper.GetStartMenuPath(userDir));
            AddIfDirectory(roots, UserProfileHelper.GetDesktopPath(userDir));
        }

        return roots;
    }

    public static IEnumerable<string> EnumerateFilesSafe(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            var dir = pending.Pop();

            string[] files;
            try
            {
                files = Directory.GetFiles(dir);
            }
            catch (Exception ex)
            {
                Logger.Log($"[StartMenuShortcutResolver] Failed to enumerate files in {dir}: {ex.Message}", LogLevel.Warn);
                continue;
            }

            foreach (var file in files)
                yield return file;

            string[] subDirs;
            try
            {
                subDirs = Directory.GetDirectories(dir);
            }
            catch (Exception ex)
            {
                Logger.Log($"[StartMenuShortcutResolver] Failed to enumerate directories in {dir}: {ex.Message}", LogLevel.Warn);
                continue;
            }

            foreach (var subDir in subDirs)
            {
                try
                {
                    var attrs = File.GetAttributes(subDir);
                    if ((attrs & FileAttributes.ReparsePoint) != 0)
                        continue;
                }
                catch
                {
                    continue;
                }
                pending.Push(subDir);
            }
        }
    }

    public static string? ResolveShortcutTarget(string shortcutPath)
    {
        if (!Path.GetExtension(shortcutPath).Equals(".lnk", StringComparison.OrdinalIgnoreCase))
            return shortcutPath;

        object? shellLinkObject = null;
        try
        {
            shellLinkObject = new ShellLink();
            var shellLink = (IShellLinkW)shellLinkObject;
            var persistFile = (IPersistFile)shellLinkObject;
            persistFile.Load(shortcutPath, 0);

            var targetPathBuilder = new StringBuilder(MAX_PATH);
            shellLink.GetPath(targetPathBuilder, targetPathBuilder.Capacity, IntPtr.Zero, SLGP_UNCPRIORITY);
            var targetPath = Environment.ExpandEnvironmentVariables(targetPathBuilder.ToString());
            return string.IsNullOrWhiteSpace(targetPath) ? null : Path.GetFullPath(targetPath);
        }
        catch (Exception ex)
        {
            Logger.Log($"[StartMenuShortcutResolver] Failed to resolve shortcut target for {shortcutPath}: {ex.Message}", LogLevel.Warn);
            return null;
        }
        finally
        {
            if (shellLinkObject != null)
                Marshal.FinalReleaseComObject(shellLinkObject);
        }
    }

    private static void AddIfDirectory(HashSet<string> roots, string path)
    {
        if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
            roots.Add(Path.GetFullPath(path));
    }

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink { }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cchMaxPath, IntPtr pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cchMaxName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cchMaxPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cchMaxPath);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cchIconPath, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }
}
