namespace Lertaro.PluginSdk.Helpers;

/// <summary>
/// SDK-level helpers for executing shell namespace actions.
/// </summary>
public static class ShellInvokeHelper
{
    /// <summary>
    /// Invokes the default verb on a virtual shell item under a parent shell folder.
    /// Required for GodMode / Control Panel items which cannot be launched via Process.Start.
    /// </summary>
    public static void InvokeShellItem(string parentShellPath, string itemPath)
    {
        object? shell = null;
        object? folder = null;
        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType == null) return;
            shell = Activator.CreateInstance(shellType);
            if (shell == null) return;

            dynamic dShell = shell;
            folder = dShell.NameSpace(parentShellPath);
            if (folder == null) return;
            dynamic dFolder = folder;

            foreach (var itemObj in dFolder.Items())
            {
                var item = itemObj;
                try
                {
                    dynamic dItem = item;
                    string p = dItem.Path;
                    if (string.Equals(p, itemPath, StringComparison.OrdinalIgnoreCase))
                    {
                        dItem.InvokeVerb();
                        return;
                    }
                }
                finally
                {
                    ReleaseComObject(item);
                }
            }
        }
        catch { }
        finally
        {
            ReleaseComObject(folder);
            ReleaseComObject(shell);
        }
    }

    private static void ReleaseComObject(object? comObject)
    {
        try
        {
            if (comObject != null && System.Runtime.InteropServices.Marshal.IsComObject(comObject))
                System.Runtime.InteropServices.Marshal.ReleaseComObject(comObject);
        }
        catch
        {
            // Best-effort cleanup; the RCW will still be reclaimed by the GC finalizer.
        }
    }
}
