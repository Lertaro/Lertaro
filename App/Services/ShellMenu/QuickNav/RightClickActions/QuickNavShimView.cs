using Lertaro.PluginSdk.Abstractions;

namespace Lertaro.App.Services.ShellMenu.QuickNav.RightClickActions;

/// <summary>
/// Minimal <see cref="IPluginSearchWindow"/> for the quick-navigation right-click menu, which is not
/// backed by a real search window. Built-in actions only use the view as a launcher service (open /
/// open-as-admin / locate) plus HideWindow, so all four members delegate to the same helpers the real
/// windows use; HideWindow closes the quick-nav menu.
/// </summary>
internal sealed class QuickNavShimView : IPluginSearchWindow
{
    private readonly Action _hide;

    public QuickNavShimView(Action hide) => _hide = hide;

    public void LocateInExplorerExternal(string path) => FileExecutor.LocateInExplorer(path);
    public void OpenFileOrFolderExternal(string path) => FileExecutor.OpenFileOrFolder(path);
    public void OpenFileOrFolderAsAdminExternal(string path) => FileExecutor.OpenFileOrFolderAsAdmin(path);
    public void HideWindow() => _hide();
}
