using System.Runtime.InteropServices;
using System.Text;

namespace Lertaro.Plugins.CoreExtensions.FileDialog;

/// <summary>
/// Whether a path handed to a dialog's edit control actually arrived.
/// </summary>
/// <remarks>
/// UIPI discards messages sent from a lower-integrity process to a higher-integrity window with no error
/// anywhere: an elevated host, or its own Open/Save dialog. The senders cannot tell from
/// <c>SendMessage(WM_SETTEXT)</c>, so the only answer is to ask the control what it holds now -- an edit that
/// never received the message reads back empty, or keeps the text it had before.
/// </remarks>
internal static class DialogEditVerifier
{
    private const uint WM_GETTEXT = 0x000D;
    private const uint SMTO_ABORTIFHUNG = 0x0002;

    /// <summary>The control's current text, or null when it did not answer.</summary>
    public static string? ReadText(IntPtr edit)
    {
        // WM_GETTEXT is marshalled by user32 itself, so the buffer stays in this process -- the opposite of
        // the app-private WM_USER+N messages, which need one allocated in the target.
        var buffer = new StringBuilder(1024);
        return SendMessageTimeout(edit, WM_GETTEXT, (IntPtr)buffer.Capacity, buffer,
            SMTO_ABORTIFHUNG, 200, out _) == IntPtr.Zero
            ? null
            // The marshaller may hand back the whole capacity, padding it with the terminator the control
            // wrote; only the text before it is the control's answer.
            : buffer.ToString().TrimEnd('\0');
    }

    /// <summary>
    /// Whether <paramref name="readBack"/> is the path that was asked for. A trailing separator is ignored on
    /// both sides: the adapters add one to a directory themselves, and a dialog is free to answer without it.
    /// Case is ignored because the dialog keeps whichever spelling of a drive or share the user typed.
    /// </summary>
    public static bool Matches(string? readBack, string wanted) =>
        readBack != null
        && string.Equals(readBack.TrimEnd('\\'), wanted.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint Msg, IntPtr wParam, StringBuilder lParam,
        uint fuFlags, uint uTimeout, out IntPtr lpdwResult);
}
