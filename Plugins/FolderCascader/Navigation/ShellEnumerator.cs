using Lertaro.PluginSdk;
using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Helpers;

namespace Lertaro.Plugins.FolderCascader.Navigation;

public static class ShellEnumerator
{
    public static void EnumerateShellFolder(string scanPath, List<DynamicMenuItem> items, Provider provider)
        => RunOnSta(() => EnumerateShellFolderCore(scanPath, items, provider));

    private static void EnumerateShellFolderCore(string scanPath, List<DynamicMenuItem> items, Provider provider)
    {
        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType == null) return;

            var shell = Activator.CreateInstance(shellType);
            if (shell == null) return;

            dynamic dShell = shell;
            var fullShellPath = scanPath.StartsWith("::") ? "shell:::" + scanPath : scanPath;
            dynamic folder = dShell.NameSpace(fullShellPath);
            if (folder == null) return;

            foreach (var item in folder.Items())
            {
                string p = item.Path;
                string name = item.Name;
                if (string.IsNullOrEmpty(p)) continue;

                if (item.IsFolder)
                {
                    items.Add(new DynamicMenuItem
                    {
                        Text = name,
                        HasSubMenu = true,
                        SubMenuHandle = provider.AllocateHandle(p),
                        HBitmapItem = IntPtr.Zero
                    });
                }
                else
                {
                    // Get the icon while we still hold the FolderItem COM object.
                    // The path string format (::{...}\0\::{...}\{guid}) cannot be
                    // re-parsed by SHParseDisplayName after the COM object is released.
                    var hBitmap = ShellPathHelper.TryGetIconHBitmapForShellItem((object)item);

                    // Capture the parent path and item path for re-enumeration on click.
                    // InvokeVerb() via Shell.Application is the only reliable way to open
                    // virtual Shell namespace items (no valid FileName for Process.Start).
                    var parentPath = fullShellPath;
                    var itemPath = p;
                    items.Add(new DynamicMenuItem
                    {
                        Text = name,
                        HBitmapItem = hBitmap,
                        OnExecute = () => InvokeShellItem(parentPath, itemPath)
                    });
                }
            }
        }
        catch { }
    }

    private static void RunOnSta(Action action)
    {
        using var done = new ManualResetEventSlim(false);
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { error = ex; }
            finally { done.Set(); }
        })
        {
            IsBackground = true,
            Name = "ShellEnumeratorSta"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        done.Wait();
        if (error != null)
            Logger.Log($"[ShellEnumerator] STA enumeration failed: {error.Message}", LogLevel.Error);
    }

    private static void InvokeShellItem(string parentPath, string itemPath)
    {
        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType == null) return;
            var shell = Activator.CreateInstance(shellType);
            if (shell == null) return;

            dynamic dShell = shell;
            dynamic folder = dShell.NameSpace(parentPath);
            if (folder == null) return;

            foreach (var item in folder.Items())
            {
                string p = item.Path;
                if (string.Equals(p, itemPath, StringComparison.OrdinalIgnoreCase))
                {
                    item.InvokeVerb();
                    return;
                }
            }
            Logger.Log($"[ShellEnumerator] InvokeShellItem: item not found for path '{itemPath}'", LogLevel.Warn);
        }
        catch (Exception ex)
        {
            Logger.Log($"[ShellEnumerator] InvokeShellItem failed: {ex.Message}", LogLevel.Error);
        }
    }
}
