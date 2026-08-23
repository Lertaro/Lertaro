using System.Diagnostics;
using System.IO;

namespace Flow.Launcher.Plugin.SharedCommands;

/// <summary>
/// Helpers for opening and exploring files and directories.
/// </summary>
public static class FilesFolders
{
    public static void OpenPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch
        {
            // Ignore failure if path is inaccessible
        }
    }

    public static void OpenFolder(string folderPath, string? selectFilePath = null)
    {
        if (string.IsNullOrWhiteSpace(folderPath) && string.IsNullOrWhiteSpace(selectFilePath))
            return;

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
        catch
        {
            // Ignore explorer launch failures
        }
    }
}
