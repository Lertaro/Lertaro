using System.Runtime.InteropServices;

namespace Lertaro.PluginSdk.Shell.FileOperations;

// Queues every source path into ONE IFileOperation batch targeting a single destination folder --
// same "one native dialog regardless of item count" shape ShellDeleteHelper uses. Copy vs move is the
// caller's own already-known answer (the clipboard's "Preferred DropEffect" marker for a paste, a fixed
// copy for the quick panel's drop target), matching Explorer's own Ctrl+V semantics exactly,
// conflict-resolution prompt (file already exists?) included via IFileOperation's own default UI.
//
// Lives in the SDK rather than in the plugin that first needed it: the app's own quick panel copies
// dropped files this way too, and a built-in surface must not stop working because a plugin was
// disabled. Both reference this project already, so it is the one place both can see.
public static class ShellPasteHelper
{
    // Fire-and-forget by design, same reasoning as ShellDeleteHelper.DeleteAsync: nothing downstream
    // needs to WAIT for the copy, and blocking the caller for however long the native progress/conflict
    // dialog sits open would freeze the search window for no reason.
    //
    // onCompleted is for the callers that do need to know it landed -- a view showing the destination
    // folder has to be told, since nothing else will tell it. Raised on the shell worker's own thread
    // once PerformOperations returns, whether it copied anything or the user cancelled the dialog: the
    // only honest signal available is "the operation is over", and a view's answer to both is the same,
    // which is to go and look again.
    public static void PasteAsync(
        IReadOnlyList<string> sourcePaths, string destinationFolder, bool move, Action? onCompleted = null)
    {
        if (sourcePaths.Count == 0) return;

        var dispatcher = ShellOperationStaWorker.StaDispatcher;
        if (dispatcher == null)
        {
            Logger.Log("[ShellPasteHelper] Shell STA worker is unavailable; paste was not performed.", LogLevel.Error);
            return;
        }

        dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                PasteCore(sourcePaths, destinationFolder, move);
            }
            finally
            {
                onCompleted?.Invoke();
            }
        }));
    }

    private static void PasteCore(IReadOnlyList<string> sourcePaths, string destinationFolder, bool move)
    {
        object? fileOpObj = null;
        try
        {
            fileOpObj = new FileOperation();
            var fileOp = (IFileOperation)fileOpObj;

            var destIid = typeof(IShellItem).GUID;
            ShellItemInterop.SHCreateItemFromParsingName(destinationFolder, IntPtr.Zero, ref destIid, out var destItem);
            try
            {
                var queued = 0;
                foreach (var path in sourcePaths)
                {
                    try
                    {
                        var iid = typeof(IShellItem).GUID;
                        ShellItemInterop.SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out var item);
                        try
                        {
                            if (move)
                                fileOp.MoveItem(item, destItem, null, null);
                            else
                                fileOp.CopyItem(item, destItem, null, null);
                            queued++;
                        }
                        finally
                        {
                            Marshal.ReleaseComObject(item);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"[ShellPasteHelper] Failed to queue {(move ? "move" : "copy")} for '{path}': {ex.Message}", LogLevel.Error);
                    }
                }

                if (queued == 0) return;
            }
            finally
            {
                Marshal.ReleaseComObject(destItem);
            }

            fileOp.PerformOperations();
        }
        catch (Exception ex)
        {
            // Includes the user cancelling the native confirmation -- IFileOperation surfaces that as
            // a COMException too, not a distinct "cancelled" signal, so this stays a log rather than
            // something surfaced to the user as an error.
            Logger.Log($"[ShellPasteHelper] Paste operation failed or was cancelled: {ex.Message}", LogLevel.Debug);
        }
        finally
        {
            if (fileOpObj != null) Marshal.ReleaseComObject(fileOpObj);
        }
    }
}
