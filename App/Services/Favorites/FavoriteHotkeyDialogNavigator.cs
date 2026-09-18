using Lertaro.Core;
using Lertaro.Core.Hook;
using Lertaro.Core.Wire;
using Lertaro.PluginSdk.Registries;

namespace Lertaro.App.Services.Favorites;

/// <summary>
/// The file-dialog half of "navigate whatever is in front": recognizing an Open/Save dialog that belongs
/// to another application rather than to a file manager, and asking the Hook to navigate it.
/// </summary>
/// <remarks>
/// Split out of <see cref="FavoriteHotkeyForegroundNavigator"/> purely to keep both files under the repo's
/// per-file line limit; this class has no state of its own and always operates on the window it is handed.
///
/// Why a dialog needs its own route: a browser/Office/Notepad "Save As" dialog is a <c>#32770</c> common
/// dialog owned by that application's process, so no inline-search adapter recognizes it -- they all match
/// a file manager's own window class -- and the file-manager route has nothing it could drive. What can
/// drive it is the app's existing <c>IFileDialogAdapter</c> set, the same adapters inline search uses to
/// put a picked result into a dialog, reached through the same <c>IpcMessageId.NavigateDialog</c> command,
/// whose Hook-side handler resolves the adapter and calls <c>NavigateTo</c> there.
/// </remarks>
internal static class FavoriteHotkeyDialogNavigator
{
    /// <summary>
    /// The dialog window at or above <paramref name="window"/> that a file-dialog adapter claims, or
    /// <see cref="IntPtr.Zero"/> when there is none.
    /// </summary>
    /// <remarks>
    /// Walks upward instead of testing the one window, mirroring <c>ExplorerWindowClassifier.
    /// FindMatchingDialogWindow</c> (private to Core, hence the same walk here): the foreground window can
    /// be a control inside the dialog rather than the frame itself, and an adapter for such a control
    /// reports that control.
    /// </remarks>
    public static IntPtr FindDialogWindow(IntPtr window)
    {
        var current = window;
        while (current != IntPtr.Zero)
        {
            try
            {
                var className = FavoriteHotkeyForegroundNavigator.GetClassName(current);
                var processName = FavoriteHotkeyForegroundNavigator.GetProcessName(current);
                if (FileDialogAdapterRegistry.GetMatchingAdapter(current, className, processName) != null)
                    return current;
            }
            catch (Exception ex)
            {
                Logger.Log($"[FavoriteHotkeys] File-dialog adapter host check threw: {ex.Message}", LogLevel.Error);
            }

            current = ExplorerNativeHooks.GetParent(current);
        }

        return IntPtr.Zero;
    }

    /// <summary>Asks the Hook to make <paramref name="dialogWindow"/> go to <paramref name="folderPath"/>.</summary>
    /// <remarks>
    /// No STA hop, unlike the file-manager route: no adapter is touched in this process at all. The Hook
    /// resolves the adapter (its enabled filter and the adapter's own "which control takes the path" logic
    /// included) and calls <c>NavigateTo</c> on a pool thread there, so sending the message is the whole of
    /// this side -- and because sending does not block, it stays on the thread the hotkey arrived on.
    /// The path is passed as configured, trailing separator included: an Open/Save adapter appends one when
    /// it needs it, so trimming here would only fight that.
    /// </remarks>
    public static void Navigate(IntPtr dialogWindow, string folderPath)
    {
        var hookClient = App.HookClient;
        if (hookClient?.IsConnected != true)
        {
            Logger.Log("[FavoriteHotkeys] The Hook process is not connected; not navigating the dialog.", LogLevel.Debug);
            return;
        }

        hookClient.SendMessage(new IpcMessage
        {
            Id = IpcMessageId.NavigateDialog,
            Hwnd = dialogWindow.ToInt64(),
            StringVal1 = folderPath
        });
    }
}
