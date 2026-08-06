using Lertaro.PluginSdk.Abstractions.Plugins.WindowAdapters;
using Lertaro.Plugins.Bandizip.Win32;

namespace Lertaro.Plugins.Bandizip;

/// <summary>
/// File-dialog integration for Bandizip's "选择解压路径" (choose extract path) dialog: lets Lertaro's own
/// path picker (the same one that drives native Open/Save dialogs -- see CoreExtensions' ClassicFileDialogAdapter
/// / FolderBrowserDialogAdapter, and WinRAR's own WinRARExtractDialogAdapter) fill in the destination path
/// field there. Detected purely by control structure (see BandizipDialogInterop.LooksLikeExtractDialog) --
/// Bandizip is localized, so nothing here reads window titles or control label text.
/// </summary>
public class BandizipExtractDialogAdapter : IFileDialogAdapter
{
    // Setting the path Edit's text and simulating the CBN_EDITCHANGE notification (see
    // BandizipDialogInterop.NotifyEditChanged) is what makes Bandizip's folder tree follow along -- but the
    // Windows Shell autocomplete popup attached to the same Edit reacts to that identical notification
    // asynchronously (confirmed live: scanning for it immediately after the SendMessage call sometimes
    // missed it entirely), not inline within the SendMessage call itself. This delay gives it time to
    // actually appear before SuppressAutoComplete goes looking for it.
    private const int AutoCompletePopupDelayMs = 150;

    public string Name => "Bandizip";

    // The destination-path field can only ever hold a folder -- never a specific file, unlike an Open/Save
    // dialog's filename box -- see IFileDialogAdapter.TargetIsFolderOnly for why callers use this.
    public bool TargetIsFolderOnly => true;

    public bool CanHandle(IntPtr hwnd, string className, string processName)
    {
        if (string.IsNullOrEmpty(className) || string.IsNullOrEmpty(processName))
            return false;

        if (!processName.Equals("Bandizip", StringComparison.OrdinalIgnoreCase))
            return false;

        if (!className.Equals("#32770", StringComparison.OrdinalIgnoreCase))
            return false;

        return BandizipDialogInterop.LooksLikeExtractDialog(hwnd);
    }

    // Reads the ComboBox itself, not its child Edit -- same reasoning as WinRARExtractDialogAdapter:
    // the combo's own WM_GETTEXT reflects whatever it displays regardless of how that text got there,
    // where the child Edit's buffer alone has been observed empty in similar dialogs elsewhere in this
    // codebase.
    public string? GetCurrentPath(IntPtr hwnd)
    {
        var combo = BandizipDialogInterop.FindPathCombo(hwnd);
        return BandizipPathHelpers.NormalizeIfWellFormed(BandizipDialogInterop.GetText(combo));
    }

    // No file-to-folder resolution here: TargetIsFolderOnly (above) tells every caller that reaches
    // NavigateTo to resolve a picked file to its containing folder themselves before ever sending it --
    // see WinRARExtractDialogAdapter.NavigateTo's own comment for why that used to be duplicated here via
    // a File.Exists check, and why that's unreliable in the elevated Hook process this runs in.
    public bool NavigateTo(IntPtr hwnd, string targetPath)
    {
        var edit = BandizipDialogInterop.FindPathEdit(hwnd);
        var combo = BandizipDialogInterop.FindPathCombo(hwnd);
        if (edit == IntPtr.Zero || combo == IntPtr.Zero) return false;

        var result = BandizipDialogInterop.SetText(edit, targetPath);
        if (!result) return false;

        BandizipDialogInterop.NotifyEditChanged(combo);
        Thread.Sleep(AutoCompletePopupDelayMs);
        BandizipDialogInterop.SuppressAutoComplete(hwnd);
        return true;
    }

    // Returns the whole dialog's bounds, not just the (single-line, short) path ComboBox's own rect: the
    // host's own docking logic rejects any rect under 100px tall as "not a real target" and falls back to
    // a fixed bottom-right-of-screen position -- see InlineSearchWindowPositioner.PositionWindowCore and
    // the identical reasoning already documented on WinRARExtractDialogAdapter.GetDockBounds.
    public bool GetDockBounds(IntPtr hwnd, out AdapterRect rect)
    {
        rect = default;
        if (hwnd == IntPtr.Zero || !BandizipDialogInterop.TryGetDialogRect(hwnd, out var r))
            return false;

        rect = new AdapterRect { Left = r.Left, Top = r.Top, Right = r.Right, Bottom = r.Bottom };
        return true;
    }

    public bool RestoreFocus(IntPtr hwnd)
    {
        var edit = BandizipDialogInterop.FindPathEdit(hwnd);
        return edit != IntPtr.Zero && BandizipDialogInterop.SetForegroundAndFocus(hwnd, edit);
    }
}
