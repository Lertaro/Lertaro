namespace Lertaro.PluginSdk.Abstractions.Plugins.WindowAdapters;

/// <summary>
/// Contract for collecting the active directory/file path from a specific window class.
/// </summary>
public interface IActivePathCollector : IPluginComponent
{

    /// <summary>
    /// Gets the localized name of the target file manager (e.g., "Windows File Explorer", "Directory Opus").
    /// </summary>
    string TargetName { get; }

    /// <summary>
    /// Checks if this collector can handle the active window with the given class name.
    /// </summary>
    bool CanHandle(string className);

    /// <summary>
    /// Checks whether this collector can handle a window identified by its class and title.
    /// </summary>
    bool CanHandle(string windowClassName, string windowTitle) => CanHandle(windowClassName);

    /// <summary>
    /// Checks whether this collector can handle a concrete window.
    /// </summary>
    bool CanHandle(IntPtr windowHwnd, string windowClassName, string processName) => CanHandle(windowClassName);

    /// <summary>
    /// <param name="activeHwnd">The currently active foreground or focused control window handle.</param>
    /// <param name="activeClassName">The class name of the active window/control.</param>
    /// <param name="windowHwnd">The top-level root owner window handle.</param>
    /// <param name="windowClassName">The class name of the top-level root window.</param>
    /// <param name="processName">The process name of the target window.</param>
    /// <returns>The collected path, or null if it could not be retrieved.</returns>
    string? TryGetPath(IntPtr activeHwnd, string activeClassName, IntPtr windowHwnd, string windowClassName, string processName);
}
