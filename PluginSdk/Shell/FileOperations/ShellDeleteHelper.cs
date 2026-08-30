using System.Runtime.InteropServices;

namespace Lertaro.PluginSdk.Shell.FileOperations;

// Queues every path into ONE IFileOperation batch (via IShellItem, which is just a "path -> COM
// object" wrapper with no notion of a common parent) so a cross-directory, cross-drive multi-select
// still gets a single native confirmation + progress dialog, exactly like Explorer's own Delete.
public static class ShellDeleteHelper
{
    // Fire-and-forget by design: nothing downstream needs to know when the delete actually finishes,
    // and blocking the caller (the UI thread, per HotkeyActionTrigger) for however long the native
    // confirm dialog sits open would freeze the search window for no reason.
    public static void DeleteAsync(IReadOnlyList<string> paths, bool permanent)
    {
        if (paths.Count == 0) return;

        var dispatcher = ShellOperationStaWorker.StaDispatcher;
        if (dispatcher == null)
        {
            Logger.Log("[ShellDeleteHelper] Shell STA worker is unavailable; delete was not performed.", LogLevel.Error);
            return;
        }

        dispatcher.BeginInvoke(new Action(() => DeleteCore(paths, permanent)));
    }

    private static void DeleteCore(IReadOnlyList<string> paths, bool permanent)
    {
        object? fileOpObj = null;
        try
        {
            fileOpObj = new FileOperation();
            var fileOp = (IFileOperation)fileOpObj;

            // Recycle: FOF_ALLOWUNDO sends it to the Recycle Bin (native "move to Recycle Bin?"
            // prompt); FOF_WANTNUKEWARNING falls back to a native permanent-delete warning instead of
            // silently going permanent on a target with no Recycle Bin (network/removable drives).
            // Permanent: no flags at all -- native "permanently delete?" prompt, no recycle attempt.
            fileOp.SetOperationFlags(permanent ? 0u : (FileOperationFlags.FOF_ALLOWUNDO | FileOperationFlags.FOF_WANTNUKEWARNING));

            var queued = 0;
            foreach (var path in paths)
            {
                try
                {
                    var iid = typeof(IShellItem).GUID;
                    ShellItemInterop.SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out var item);
                    try
                    {
                        fileOp.DeleteItem(item, null);
                        queued++;
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(item);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log($"[ShellDeleteHelper] Failed to queue delete for '{path}': {ex.Message}", LogLevel.Error);
                }
            }

            if (queued == 0) return;

            fileOp.PerformOperations();
        }
        catch (Exception ex)
        {
            // Includes the user cancelling the native confirmation -- IFileOperation surfaces that as
            // a COMException too, not a distinct "cancelled" signal, so this stays a log rather than
            // something surfaced to the user as an error.
            Logger.Log($"[ShellDeleteHelper] Delete operation failed or was cancelled: {ex.Message}", LogLevel.Debug);
        }
        finally
        {
            if (fileOpObj != null) Marshal.ReleaseComObject(fileOpObj);
        }
    }
}
