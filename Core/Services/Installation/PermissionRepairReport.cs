using System.Text;

namespace Lertaro.Core.Services.Installation;

/// <summary>
/// The one channel an elevated <c>--repair-permissions</c> run can leave a trace in.
/// </summary>
/// <remarks>
/// The repair is launched hidden, with <c>UseShellExecute = true</c> and <c>Verb = "runas"</c> -- exactly the
/// combination that cannot have its stderr redirected -- and it deliberately runs BEFORE the service opens its
/// log, because opening a log is one of the writes the lock walk exists to secure. So its failure used to reach
/// the user as nothing but "permission repair did not complete successfully": no log, no stderr, no way to tell
/// which step refused (issue #342).
///
/// A small append-only file under the invoking user's temp folder is what is left. It needs no privilege, no
/// ACL of its own, and no open log; the App -- same user, same session -- reads it back and puts the real
/// reason in front of the user. Every call happens BEFORE the step it names, so a process killed for running
/// too long still leaves the step it was stuck on as its last line.
/// </remarks>
public static class PermissionRepairReport
{
    // ponytail: capped, because the path is one any local process can write to. A run that finds it oversized
    // starts over rather than appending to whatever is already there.
    private const int MaximumBytes = 16 * 1024;

    // No BOM: this is a line-oriented diagnostics file that another process may read back byte for byte.
    private static readonly Encoding Text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    public static string DefaultPath => Path.Combine(Path.GetTempPath(), "Lertaro", "repair.log");

    /// <summary>Drops the previous run's trace, so what is read back belongs to the run just started.</summary>
    public static void Clear(string? path = null) => TryRun(() => File.Delete(path ?? DefaultPath));

    /// <summary>Records the step about to run, or why one failed.</summary>
    public static void Write(string message, string? path = null)
    {
        var file = path ?? DefaultPath;
        TryRun(() =>
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            if (new FileInfo(file) is { Exists: true, Length: > MaximumBytes }) File.Delete(file);
            File.AppendAllText(file, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{Environment.ProcessId}] {message}{Environment.NewLine}", Text);
        });
    }

    /// <summary>The trace, or null when there is none worth showing. Never throws: this is diagnostics.</summary>
    public static string? Read(string? path = null)
    {
        try
        {
            var file = path ?? DefaultPath;
            if (!File.Exists(file)) return null;
            var text = File.ReadAllText(file, Text).Trim();
            return text.Length == 0 ? null : text;
        }
        catch { return null; }
    }

    /// <summary>The trace's last line: on a run that was killed, the step it was still on.</summary>
    public static string? LastStep(string? report) =>
        string.IsNullOrWhiteSpace(report)
            ? null
            : report.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault();

    private static void TryRun(Action action)
    {
        try { action(); }
        catch { /* diagnostics must never be the reason a repair fails */ }
    }
}
