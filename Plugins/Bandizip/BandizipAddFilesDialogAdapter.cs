using Lertaro.PluginSdk.Abstractions.Plugins.WindowAdapters;
using Lertaro.Plugins.Bandizip.Win32;

namespace Lertaro.Plugins.Bandizip;

/// <summary>
/// File-dialog integration for Bandizip's "选择" (choose files to add to archive) dialog: lets Lertaro's
/// own path picker navigate it to a folder, same idea as BandizipExtractDialogAdapter but for a completely
/// different dialog template -- this one has no ComboBox at all, just a plain Edit for the path, and (unlike
/// the extract dialog) a plain WM_SETTEXT alone is enough to make its folder tree follow along, confirmed
/// live. Detected purely by control structure (see BandizipDialogInterop.LooksLikeAddFilesDialog) -- Bandizip
/// is localized, so nothing here reads window titles or control label text.
/// </summary>
public class BandizipAddFilesDialogAdapter : IFileDialogAdapter
{
    public string Name => "Bandizip";

    // This dialog only ever navigates to a folder to browse within -- the user picks the actual file(s) to
    // add from its list themselves, same folder-only contract as BandizipExtractDialogAdapter.
    public bool TargetIsFolderOnly => true;

    public bool CanHandle(IntPtr hwnd, string className, string processName)
    {
        if (string.IsNullOrEmpty(className) || string.IsNullOrEmpty(processName))
            return false;

        if (!processName.Equals("Bandizip", StringComparison.OrdinalIgnoreCase))
            return false;

        if (!className.Equals("#32770", StringComparison.OrdinalIgnoreCase))
            return false;

        return BandizipDialogInterop.LooksLikeAddFilesDialog(hwnd);
    }

    public string? GetCurrentPath(IntPtr hwnd)
    {
        var edit = BandizipDialogInterop.FindAddFilesPathEdit(hwnd);
        return BandizipPathHelpers.NormalizeIfWellFormed(BandizipDialogInterop.GetText(edit));
    }

    // No file-to-folder resolution here: TargetIsFolderOnly (above) tells every caller that reaches
    // NavigateTo to resolve a picked file to its containing folder themselves before ever sending it --
    // see WinRARExtractDialogAdapter.NavigateTo's own comment for why that used to be duplicated here via
    // a File.Exists check, and why that's unreliable in the elevated Hook process this runs in.
    public bool NavigateTo(IntPtr hwnd, string targetPath)
    {
        var edit = BandizipDialogInterop.FindAddFilesPathEdit(hwnd);
        return edit != IntPtr.Zero && BandizipDialogInterop.SetText(edit, targetPath);
    }

    // Returns the whole dialog's bounds, not just the path Edit's own rect -- same reasoning as
    // BandizipExtractDialogAdapter.GetDockBounds.
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
        var edit = BandizipDialogInterop.FindAddFilesPathEdit(hwnd);
        return edit != IntPtr.Zero && BandizipDialogInterop.SetForegroundAndFocus(hwnd, edit);
    }
}
