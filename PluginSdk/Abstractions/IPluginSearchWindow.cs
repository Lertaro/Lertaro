namespace Lertaro.PluginSdk.Abstractions;

/// <summary>
/// Minimal search window interface exposed to plugins.
/// </summary>
public interface IPluginSearchWindow
{
    /// <summary>Locates the specified path in Windows Explorer.</summary>
    void LocateInExplorerExternal(string path);

    /// <summary>Opens the specified file or folder using default application.</summary>
    void OpenFileOrFolderExternal(string path);

    /// <summary>Opens the specified file or folder with administrator privileges.</summary>
    void OpenFileOrFolderAsAdminExternal(string path);

    /// <summary>Closes or hides the search window.</summary>
    void HideWindow();
}
