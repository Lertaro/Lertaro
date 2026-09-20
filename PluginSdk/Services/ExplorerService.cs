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
    /// Delegate assigned by the host application to open a folder the way the app opens folders.
    /// </summary>
    /// <remarks>
    /// A folder is not just "a path the shell can open": the host may have a default file manager
    /// configured for it, and an option to put it in a new tab of an already-open Explorer window (with a
    /// documented fallback when that is not possible). None of that is reachable from a plugin, so a plugin
    /// that opens a folder asks the host instead of calling the shell itself -- the same reason
    /// <see cref="OpenDirectoryFunc"/> exists. Null in a process that never wired it (the Hook), where
    /// <see cref="OpenFolder"/> falls back to the shell.
    /// </remarks>
    public static Action<string>? OpenFolderFunc { get; set; }

    /// <summary>
    /// Opens a FOLDER through whatever route the host opens folders with, falling back to the shell.
    /// </summary>
    /// <remarks>
    /// For folders only -- a file belongs to its associated program, not to this route. Callers keep their
    /// own file handling, and hand only a directory to this.
    /// </remarks>
    public static void OpenFolder(string? folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath)) return;

        var openThroughHost = OpenFolderFunc;
        if (openThroughHost != null)
        {
            openThroughHost(folderPath);
            return;
        }

        ShellOpenHelper.TryOpenFolder(folderPath);
    }

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
