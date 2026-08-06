using System.IO;
using Lertaro.PluginSdk.Abstractions.Plugins.WindowAdapters;
using Lertaro.Plugins.WinRAR.Win32;

namespace Lertaro.Plugins.WinRAR;

/// <summary>
/// File-dialog integration for WinRAR's "Extract path and options" dialog: lets Lertaro's own path
/// picker (the same one that drives native Open/Save dialogs -- see CoreExtensions' ClassicFileDialogAdapter
/// / FolderBrowserDialogAdapter) fill in the destination path field there. Detected purely by control
/// structure (see WinRARDialogInterop.LooksLikeExtractDialog) -- WinRAR is localized, so nothing here reads
/// window titles or control label text.
/// </summary>
public class WinRARExtractDialogAdapter : IFileDialogAdapter
{
    public string Name => "WinRAR";

    // The destination-path field can only ever hold a folder -- never a specific file, unlike an Open/Save
    // dialog's filename box -- see IFileDialogAdapter.TargetIsFolderOnly for why callers use this.
    public bool TargetIsFolderOnly => true;

    public bool CanHandle(IntPtr hwnd, string className, string processName)
    {
        if (string.IsNullOrEmpty(className) || string.IsNullOrEmpty(processName))
            return false;

        if (!processName.Equals("WinRAR", StringComparison.OrdinalIgnoreCase))
            return false;

        if (!className.Equals("#32770", StringComparison.OrdinalIgnoreCase))
            return false;

        return WinRARDialogInterop.LooksLikeExtractDialog(hwnd);
    }

    // Pure normalize-and-check, pulled out so it's unit-testable without a live WinRAR window --
    // GetCurrentPath itself just supplies the live GetText() call around it. Deliberately does NOT verify
    // the path actually exists via Directory.Exists: this runs in the elevated Hook process (see
    // ExplorerActivePathPoller.Poll, the poller that calls GetCurrentPath on every tick), where UAC's split
    // token puts it in a different logon session than whatever mapped any network drive letters -- a
    // perfectly real Y:\... path the interactive user can see would otherwise resolve to "doesn't exist"
    // there, silently freezing SearchScope at its last value forever once the dialog's target moves onto a
    // network drive (confirmed live). ExplorerInlineSearchAdapter.cs's own ExecuteItem hit and documented
    // this identical Directory.Exists-in-the-elevated-Hook-process trap already -- same fix here: trust
    // that it's well-formed (syntactically rooted) rather than trying to verify it.
    internal static string? NormalizeIfWellFormed(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var trimmed = text.TrimEnd('\\', '/');
        // "C:" alone is NOT the same path as "C:\" -- it means "the current directory on drive C" (a
        // per-process, per-drive concept), not that drive's root. TrimEnd above would otherwise turn a
        // genuine root path into that different, ambiguous form; confirmed live via app.log showing
        // SearchScope='D:' (missing its trailing backslash) feeding a garbled result into
        // Path.GetRelativePath downstream.
        if (trimmed.Length == 2 && trimmed[1] == ':') trimmed += '\\';
        return Path.IsPathRooted(trimmed) ? trimmed : null;
    }

    // Reads the ComboBox itself, not its child Edit: confirmed empirically that when the dialog's
    // displayed path comes from the combo's own current selection (e.g. its default on a freshly opened
    // dialog) rather than something the user actually typed, WM_GETTEXT on the child Edit alone returns
    // empty even though the combo visibly shows a real path. The combo's own WM_GETTEXT reflects
    // whatever it displays either way. (WinRARDialogInterop.GetText's own SendMessageTimeout separately
    // fixed an intermittent empty-read regardless of which control this queries.)
    public string? GetCurrentPath(IntPtr hwnd)
    {
        var combo = WinRARDialogInterop.FindPathCombo(hwnd);
        return NormalizeIfWellFormed(WinRARDialogInterop.GetText(combo));
    }

    // No file-to-folder resolution here: TargetIsFolderOnly (above) tells every caller that reaches
    // NavigateTo -- InlineSearchNavigator.RunFallbackChain and QuickNavigationNavigator.NavigateOrOpen are
    // the only two -- to resolve a picked file to its containing folder themselves before ever sending it.
    // That used to be duplicated here too (a File.Exists-based check), but File.Exists is unreliable for
    // this in the elevated Hook process this method actually runs in: a network drive the interactive user
    // mapped without elevation can come back "doesn't exist" even for a perfectly real file. Resolving once,
    // in the process that can actually see it, is strictly more correct than any local fallback here.
    public bool NavigateTo(IntPtr hwnd, string targetPath)
    {
        var edit = WinRARDialogInterop.FindPathEdit(hwnd);
        if (edit == IntPtr.Zero) return false;

        var result = WinRARDialogInterop.SetText(edit, targetPath);
        if (result)
            WinRARDialogInterop.ClickDisplayButton(hwnd);
        return result;
    }

    // Returns the whole dialog's bounds, not just the (single-line, ~27px-tall) path ComboBox's own rect:
    // the host's own docking logic rejects any rect under 100px tall as "not a real target" (see
    // InlineSearchWindowPositioner.PositionWindowCore's hasValidRect check) and falls back to a
    // fixed bottom-right-of-screen position -- confirmed empirically the ComboBox's rect alone was silently
    // hitting exactly that fallback. Same whole-dialog convention ClassicFileDialogAdapter/
    // FolderBrowserDialogAdapter already use for Explorer's own common dialogs.
    public bool GetDockBounds(IntPtr hwnd, out AdapterRect rect)
    {
        rect = default;
        if (hwnd == IntPtr.Zero || !WinRARDialogInterop.TryGetDialogRect(hwnd, out var r))
            return false;

        rect = new AdapterRect { Left = r.Left, Top = r.Top, Right = r.Right, Bottom = r.Bottom };
        return true;
    }

    public bool RestoreFocus(IntPtr hwnd)
    {
        var edit = WinRARDialogInterop.FindPathEdit(hwnd);
        return edit != IntPtr.Zero && WinRARDialogInterop.SetForegroundAndFocus(hwnd, edit);
    }
}
