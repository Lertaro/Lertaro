using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Lertaro.PluginSdk.Helpers;

namespace Lertaro.Plugins.Tessoa;

internal sealed record TessoaWindow(nint Handle, int Pid, string Image)
{
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out int processId);

    [DllImport("user32.dll")]
    private static extern nint GetAncestor(nint window, uint flags);

    internal static TessoaWindow? From(nint hwnd)
    {
        if (hwnd == 0 || GetAncestor(hwnd, 2) != hwnd || GetWindowThreadProcessId(hwnd, out var pid) == 0)
            return null;
        try
        {
            using var process = Process.GetProcessById(pid);
            if (!process.ProcessName.Equals("tessoa", StringComparison.OrdinalIgnoreCase)) return null;
            var image = process.MainModule?.FileName;
            return image != null && Path.GetFileName(image).Equals("tessoa.exe", StringComparison.OrdinalIgnoreCase)
                ? new(hwnd, pid, image) : null;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or ArgumentException) { return null; }
    }

    internal static IEnumerable<TessoaWindow> Visible() => OpenFolderWindowEnumerator.FindVisibleWindows(
        hwnd => OpenFolderWindowEnumerator.GetProcessName(hwnd).Equals("tessoa", StringComparison.OrdinalIgnoreCase))
        .Select(From).OfType<TessoaWindow>();
}
