using System.Runtime.InteropServices;

namespace Lertaro.PluginSdk.Helpers;

/// <summary>
/// Validates Shell namespace paths without converting them to physical paths first.
/// Some valid virtual folders, such as This PC, have no filesystem path at all.
/// </summary>
public static class ShellVirtualPathValidator
{
    private const uint SfgaoFolder = 0x20000000;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName(
        [MarshalAs(UnmanagedType.LPWStr)] string name,
        IntPtr bindingContext,
        out IntPtr itemIdList,
        uint attributesToRetrieve,
        out uint attributes);

    public static bool Exists(string? path, bool requireFolder = false)
    {
        if (string.IsNullOrWhiteSpace(path)
            || (!path.StartsWith("::", StringComparison.Ordinal)
                && !path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var itemIdList = IntPtr.Zero;
        try
        {
            var result = SHParseDisplayName(path.Trim(), IntPtr.Zero, out itemIdList,
                requireFolder ? SfgaoFolder : 0, out var attributes);
            return result == 0
                && itemIdList != IntPtr.Zero
                && (!requireFolder || (attributes & SfgaoFolder) != 0);
        }
        catch
        {
            return false;
        }
        finally
        {
            if (itemIdList != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(itemIdList);
            }
        }
    }
}
