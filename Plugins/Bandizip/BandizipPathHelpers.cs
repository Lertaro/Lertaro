using System.IO;

namespace Lertaro.Plugins.Bandizip;

// Shared by BandizipExtractDialogAdapter and BandizipAddFilesDialogAdapter -- both need the identical
// normalize-and-check logic, so it lives here once instead of copied per adapter.
internal static class BandizipPathHelpers
{
    // Pure normalize-and-check, pulled out so it's unit-testable without a live Bandizip window --
    // GetCurrentPath itself just supplies the live GetText() call around it. Deliberately does NOT verify
    // the path actually exists via Directory.Exists: this runs in the elevated Hook process (see
    // ExplorerActivePathPoller.Poll, the poller that calls GetCurrentPath on every tick), where UAC's split
    // token puts it in a different logon session than whatever mapped any network drive letters -- a
    // perfectly real Y:\... path the interactive user can see would otherwise resolve to "doesn't exist"
    // there, silently freezing SearchScope at its last value forever once the dialog's target moves onto a
    // network drive (confirmed live). ExplorerInlineSearchAdapter.cs's own ExecuteItem hit and documented
    // this identical Directory.Exists-in-the-elevated-Hook-process trap already -- same fix here: trust
    // that it's well-formed (syntactically rooted) rather than trying to verify it.
    public static string? NormalizeIfWellFormed(string text)
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
}
