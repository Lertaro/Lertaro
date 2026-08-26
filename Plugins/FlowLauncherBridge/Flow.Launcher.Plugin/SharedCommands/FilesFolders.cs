using System.Diagnostics;
using System.IO;
using System.Windows;

namespace Flow.Launcher.Plugin.SharedCommands;

public static class FilesFolders
{
    public static void OpenPath(string path, Func<string, MessageBoxResult>? messageBoxExShow = null)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch { }
    }

    public static Action<string, string?>? OpenFolderFunc { get; set; }

    public static void OpenFolder(string folderPath, string? selectFilePath = null)
    {
        if (OpenFolderFunc != null)
        {
            OpenFolderFunc(folderPath, selectFilePath);
            return;
        }

        if (string.IsNullOrWhiteSpace(folderPath) && string.IsNullOrWhiteSpace(selectFilePath)) return;
        try
        {
            if (!string.IsNullOrWhiteSpace(selectFilePath) && File.Exists(selectFilePath))
            {
                Process.Start("explorer.exe", $"/select,\"{selectFilePath}\"");
            }
            else if (!string.IsNullOrWhiteSpace(folderPath) && Directory.Exists(folderPath))
            {
                Process.Start("explorer.exe", $"\"{folderPath}\"");
            }
        }
        catch { }
    }

    public static void OpenFile(string filePath, string workingDirectory = "", bool asAdmin = false, Func<string, MessageBoxResult>? messageBoxExShow = null)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return;
        try
        {
            var psi = new ProcessStartInfo(filePath) { UseShellExecute = true };
            if (!string.IsNullOrWhiteSpace(workingDirectory)) psi.WorkingDirectory = workingDirectory;
            if (asAdmin) psi.Verb = "runas";
            Process.Start(psi);
        }
        catch { }
    }

    public static bool LocationExists(string? path) => !string.IsNullOrWhiteSpace(path) && (Directory.Exists(path) || File.Exists(path));
    public static bool FileExists(string? path) => !string.IsNullOrWhiteSpace(path) && File.Exists(path);
    public static bool FileOrLocationExists(string? path) => LocationExists(path);
    public static bool IsZipFilePath(string? path, bool checkExists = false) => !string.IsNullOrWhiteSpace(path) && path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
    public static bool IsLocationPathString(string? path) => !string.IsNullOrWhiteSpace(path) && (path.Contains(Path.DirectorySeparatorChar) || path.Contains(Path.AltDirectorySeparatorChar));

    public static string GetPreviousExistingDirectory(Func<string, bool> directoryExists, string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        var dir = Path.GetDirectoryName(path);
        while (!string.IsNullOrEmpty(dir) && !directoryExists(dir))
        {
            dir = Path.GetDirectoryName(dir);
        }
        return dir ?? string.Empty;
    }

    public static string ReturnPreviousDirectoryIfIncompleteString(string path) => GetPreviousExistingDirectory(Directory.Exists, path);
    public static bool PathContains(string path, string containedPath, bool exactMatch = false) => !string.IsNullOrWhiteSpace(path) && !string.IsNullOrWhiteSpace(containedPath) && path.Contains(containedPath, StringComparison.OrdinalIgnoreCase);
    public static string EnsureTrailingSlash(string path) => string.IsNullOrWhiteSpace(path) ? string.Empty : (path.EndsWith(Path.DirectorySeparatorChar) ? path : path + Path.DirectorySeparatorChar);
    public static void ValidateDirectory(string path) { if (!string.IsNullOrWhiteSpace(path) && !Directory.Exists(path)) Directory.CreateDirectory(path); }
    public static void ValidateDataDirectory(string parentPath, string directoryName) => ValidateDirectory(Path.Combine(parentPath, directoryName));
    public static string ResolveAbsolutePath(string path) => Path.GetFullPath(path);

    public static void CopyAll(this string sourcePath, string targetPath, Func<string, MessageBoxResult>? messageBoxExShow = null)
    {
        if (!Directory.Exists(sourcePath)) return;
        Directory.CreateDirectory(targetPath);
        foreach (var file in Directory.GetFiles(sourcePath))
        {
            File.Copy(file, Path.Combine(targetPath, Path.GetFileName(file)), true);
        }
        foreach (var dir in Directory.GetDirectories(sourcePath))
        {
            CopyAll(dir, Path.Combine(targetPath, Path.GetFileName(dir)), messageBoxExShow);
        }
    }

    public static bool VerifyBothFolderFilesEqual(string path1, string path2, Func<string, MessageBoxResult>? messageBoxExShow = null) => Directory.Exists(path1) && Directory.Exists(path2);
    public static void RemoveFolderIfExists(string path, Func<string, MessageBoxResult>? messageBoxExShow = null) { if (Directory.Exists(path)) Directory.Delete(path, true); }
    public static bool TryDeleteDirectoryRobust(string path, int maxAttempts = 3, int delayMs = 100)
    {
        if (!Directory.Exists(path)) return true;
        try { Directory.Delete(path, true); return true; } catch { return false; }
    }
}
