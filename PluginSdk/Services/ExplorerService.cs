using System.Diagnostics;
using System.IO;

namespace Lertaro.PluginSdk.Services;

/// <summary>
/// Provides shell file and folder navigation operations, respecting host-configured file managers.
/// </summary>
public static class ExplorerService
{
    /// <summary>
    /// Delegate assigned by the host application to open a directory or locate a file.
    /// </summary>
    public static Action<string, string?>? OpenDirectoryFunc { get; set; }

    /// <summary>
    /// Opens the specified directory or selects the specified file, using the host's configured file manager if enabled.
    /// </summary>
    public static void OpenDirectory(string directoryPath, string? fileNameOrFilePath = null)
    {
        if (OpenDirectoryFunc != null)
        {
            OpenDirectoryFunc(directoryPath, fileNameOrFilePath);
            return;
        }

        if (string.IsNullOrWhiteSpace(directoryPath)) return;
        try
        {
            if (!string.IsNullOrWhiteSpace(fileNameOrFilePath) && File.Exists(fileNameOrFilePath))
                Process.Start("explorer.exe", $"/select,\"{fileNameOrFilePath}\"");
            else
                Process.Start("explorer.exe", $"\"{directoryPath}\"");
        }
        catch { }
    }
}
