using System.IO;
using Lertaro.PluginSdk.Helpers;

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

        // Naming an item that is still there means "show me that item", which the shell does by selecting
        // it inside its own folder. Opening the folder stays the answer for everything else -- including a
        // named item that has since been deleted, where selecting nothing is not a useful outcome.
        if (ShouldRevealItem(fileNameOrFilePath, File.Exists) && ShellOpenHelper.TryRevealInFolder(fileNameOrFilePath))
            return;

        ShellOpenHelper.TryOpenFolder(directoryPath);
    }

    /// <summary>
    /// Whether this call should reveal the named item rather than open the directory.
    /// </summary>
    /// <remarks>
    /// Split out from <see cref="OpenDirectory"/> so the branch is covered by a test: the rest of that
    /// method is a hand-off to the shell, which needs a live desktop.
    /// </remarks>
    internal static bool ShouldRevealItem(string? fileNameOrFilePath, Func<string, bool> fileExists)
        => !string.IsNullOrWhiteSpace(fileNameOrFilePath) && fileExists(fileNameOrFilePath);
}
