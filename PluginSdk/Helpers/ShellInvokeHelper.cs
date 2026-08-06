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
        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType == null) return;
            var shell = Activator.CreateInstance(shellType);
            if (shell == null) return;

            dynamic dShell = shell;
            dynamic folder = dShell.NameSpace(parentShellPath);
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
        }
        catch { }
    }
}
