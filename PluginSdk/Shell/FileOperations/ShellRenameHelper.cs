using System.Runtime.InteropServices;

namespace Lertaro.PluginSdk.Shell.FileOperations;

/// <summary>Queues a single rename through the Windows Shell's IFileOperation API.</summary>
public static class ShellRenameHelper
{
    public static void RenameAsync(string path, string newName)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(newName)) return;

        var dispatcher = ShellOperationStaWorker.StaDispatcher;
        if (dispatcher == null)
        {
            Logger.Log("[ShellRenameHelper] Shell STA worker is unavailable; rename was not performed.", LogLevel.Error);
            return;
        }

        dispatcher.BeginInvoke(new Action(() => RenameCore(path, newName)));
    }

    private static void RenameCore(string path, string newName)
    {
        object? fileOpObj = null;
        try
        {
            fileOpObj = new FileOperation();
            var fileOp = (IFileOperation)fileOpObj;
            var iid = typeof(IShellItem).GUID;
            ShellItemInterop.SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out var item);
            try
            {
                fileOp.RenameItem(item, newName, null);
                fileOp.PerformOperations();
            }
            finally
            {
                Marshal.ReleaseComObject(item);
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"[ShellRenameHelper] Rename operation failed for '{path}': {ex.Message}", LogLevel.Debug);
        }
        finally
        {
            if (fileOpObj != null) Marshal.ReleaseComObject(fileOpObj);
        }
    }
}
