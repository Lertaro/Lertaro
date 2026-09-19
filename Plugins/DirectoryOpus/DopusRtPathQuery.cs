using System.Diagnostics;
using System.IO;
using Lertaro.PluginSdk;
using Lertaro.PluginSdk.Services;
using Microsoft.Win32;

namespace Lertaro.Plugins.DirectoryOpus;

/// <summary>
/// Reads the folders Opus is showing through its own documented interface --
/// <c>dopusrt.exe /info &lt;file&gt;,paths</c>, which Opus documents as "a list of paths currently
/// displayed in all tabs in all Listers" -- instead of scraping the file-display windows for their text.
/// The answer itself is read by <see cref="DopusPathsXml"/>; where the tool writes it is
/// <see cref="DopusRtOutputFile"/>'s business.
/// </summary>
/// <remarks>
/// The window-text fallback this replaced could only see the tabs whose container window happened to be
/// visible, and had no way to tell which tab of which group was the active one; the documented output
/// carries the folder in each element's own text and the grouping in its attributes (<c>lister</c>,
/// <c>side</c>, <c>active_tab</c>).
/// One process launch per query, so callers must not put this on a per-keystroke path; it is a snapshot
/// request (the opened-folders list), not a live poll.
/// </remarks>
internal static class DopusRtPathQuery
{
    private const string OpusKey = @"SOFTWARE\GPSoftware\Directory Opus";
    private const string ToolName = "dopusrt.exe";

    /// <summary>
    /// The tabs Opus reports, already ordered, or null when Opus or its tool could not answer -- a
    /// missing installation, Opus not running, or output that is not the XML this expects. Null means
    /// "ask the window-scraping fallback instead", never "there are no folders".
    /// </summary>
    public static IReadOnlyList<DopusTab>? TryReadTabs()
    {
        // Opus itself has to be running, and that is a precondition rather than a nicety: dopusrt.exe is
        // Opus's own runtime helper and RUNNING IT IS WHAT STARTS OPUS when it is stopped. Merely being
        // installed is not enough, which is why this is checked here and not left to FindTool -- that
        // deliberately still resolves the tool path for an installed-but-stopped Opus (its registry and
        // Program Files fallbacks exist for the ELEVATED case, where a running Opus hides its own folder
        // from MainModule), so every startup of Lertaro used to run the tool and conjure an Opus window.
        //
        // Nothing is lost by skipping: a stopped Opus has no tabs to report, so the answer could only ever
        // have been empty, and a lister that is somehow on screen is still found by the window scrape the
        // caller falls back to.
        if (!IsOpusRunning()) return null;

        var tool = FindTool();
        if (tool == null) return null;

        var output = DopusRtOutputFile.Create();
        if (output == null)
        {
            // Opus's own rules for this path (ASCII, no double quote, a writable directory, a file nobody
            // else holds) are not satisfiable here, so the query cannot be made safely at all. The scrape
            // fallback is the honest answer rather than handing Opus a path it will refuse.
            Logger.Log("[DirectoryOpus] no usable output path for the dopusrt query; skipping it.", LogLevel.Debug);
            return null;
        }

        try
        {
            // The HOST runs dopusrt, not this process. The Hook is started elevated, and an elevated
            // dopusrt can never be answered by the unelevated Opus -- User Interface Privilege Isolation
            // blocks the reply, so it hangs forever and writes nothing -- while starting a process at a
            // lower integrity level needs a privilege the Hook does not hold. The host knows how to reach a
            // context that CAN run it (the App, at the user's own level), so the request goes there.
            var text = RunViaHost(tool, output);
            if (text == null)
            {
                RunTool(tool, output, out var exited);
                text = WaitForOutput(output, exited);
            }

            if (text == null) return null;

            // Opus answers an empty <results .../> when nothing is open, which is a valid "no tabs".
            return DopusPathsXml.OrderTabs(DopusPathsXml.ParseTabs(text));
        }
        catch (Exception ex)
        {
            Logger.Log($"[DirectoryOpus] dopusrt paths query failed: {ex.Message}", LogLevel.Debug);
            return null;
        }
        finally
        {
            try { File.Delete(output); } catch { /* our own temp file; deleting is best effort */ }
        }
    }

    /// <summary>
    /// Asks the host to run dopusrt and returns what it wrote, or null when no host runner is wired up or
    /// it produced nothing.
    /// </summary>
    /// <remarks>
    /// The path is normalised because it is the key the host matches its answer by: a trailing separator
    /// would otherwise make the two spellings different strings for the same file. The host removes the
    /// file itself, so only the text comes back.
    /// </remarks>
    private static string? RunViaHost(string tool, string output)
    {
        var run = ToolRunService.RunDopusPathsFunc;
        if (run == null) return null;

        try
        {
            var normalized = Path.GetFullPath(output);
            return run(tool, normalized).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Logger.Log($"[DirectoryOpus] the host could not run dopusrt: {ex.Message}", LogLevel.Debug);
            return null;
        }
    }

    /// <summary>
    /// Waits for dopusrt to finish writing and returns what it wrote, or null when no usable file
    /// appeared. The file's content is the whole verdict: dopusrt exits 0 whether or not it wrote
    /// anything, so its exit code is never consulted.
    /// </summary>
    /// <param name="path">The file pre-created for this query.</param>
    /// <param name="exited">
    /// Whether the tool finished on its own rather than being killed for exceeding the bound. When it
    /// exited, it wrote before doing so and gets only a short grace period: an empty file at that point
    /// will stay empty, and waiting longer only blocks the caller.
    /// </param>
    private static string? WaitForOutput(string path, bool exited)
    {
        // A run that exited on its own has already had its chance, so it gets the shorter wait; the extra
        // attempts are for a run that was killed, which may have been killed mid-write. Both are short
        // because the caller is the Hook, where a stall costs the whole foreground pipeline.
        var attempts = exited ? 10 : 15;
        var delayMs = 20;

        for (var attempt = 0; attempt < attempts; attempt++)
        {
            try
            {
                var file = new FileInfo(path);
                if (file.Exists && file.Length > 0)
                {
                    var text = File.ReadAllText(path);
                    if (!string.IsNullOrWhiteSpace(text)) return text;
                }
            }
            catch (IOException) { /* still being written, or briefly locked by dopusrt: try again */ }
            catch (UnauthorizedAccessException) { /* same */ }

            Thread.Sleep(delayMs);
        }

        Logger.Log(
            $"[DirectoryOpus] dopusrt produced no usable output at '{path}' (exited={exited}); its exit code says nothing either way.",
            LogLevel.Debug);
        return null;
    }

    /// <summary>
    /// Runs one documented query. The argument is <c>&lt;output file&gt;,&lt;command&gt;</c> with the path
    /// QUOTED, which is what makes a space-bearing path work; everything else about the argument is
    /// literal, so the path is handed over exactly as validated.
    /// </summary>
    /// <param name="exited">
    /// Whether the tool finished on its own. False means it was killed for exceeding the bound, which is
    /// reported to the caller so it need not wait for a file from a process that never wrote one.
    /// </param>
    private static void RunTool(string tool, string output, out bool exited)
    {
        // Started as-is: this is the fallback for when no host runner is wired up (a unit test, or a host
        // that cannot answer), and it is only ever correct where this process is already at the user's own
        // privilege level. See RunViaHost for the case that matters in the shipped app.
        var arguments = $"/info \"{output}\",paths";

        using var process = Process.Start(new ProcessStartInfo(tool)
        {
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true
        });

        if (process == null)
        {
            exited = true;
            return;
        }

        // A working dopusrt finishes in well under a second, so this bound only ever bites when it is
        // wedged. That is not hypothetical: measured inside the app it launched and never exited, and the
        // old 3s bound -- followed by a 2.2s wait because the caller assumed it was still writing -- held
        // the caller for over five seconds per query.
        exited = process.WaitForExit(1500);
        if (!exited)
        {
            try { process.Kill(); } catch { /* best effort */ }
        }
    }

    /// <summary>
    /// Whether Directory Opus itself is running, as opposed to merely installed.
    /// </summary>
    /// <remarks>
    /// A presence test, not a handle to anything: an elevated Opus is still running, and asking it for its
    /// paths is exactly the case the host runner exists for. The processes are disposed so the enumeration
    /// does not hold handles open on every window of the user's session.
    /// </remarks>
    private static bool IsOpusRunning()
    {
        Process[] processes;
        try { processes = Process.GetProcessesByName("dopus"); }
        catch { return false; }

        try { return processes.Length > 0; }
        finally { foreach (var process in processes) process.Dispose(); }
    }

    /// <summary>
    /// Locates <c>dopusrt.exe</c>. Tries the running Opus process first (its own folder is
    /// authoritative) and then the installation paths Opus records in the registry, because the plugin
    /// also runs where Opus is installed but not yet started.
    /// </summary>
    private static string? FindTool()
    {
        foreach (var process in Process.GetProcessesByName("dopus"))
        {
            try
            {
                var directory = Path.GetDirectoryName(process.MainModule?.FileName);
                var tool = Combine(directory);
                if (tool != null) return tool;
            }
            catch { /* elevated Opus: fall through to the registry */ }
            finally { process.Dispose(); }
        }

        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            try
            {
                using var key = hive.OpenSubKey(OpusKey);
                foreach (var name in key?.GetValueNames() ?? Array.Empty<string>())
                {
                    if (key?.GetValue(name) is not string value) continue;
                    if (!value.Contains("Directory Opus", StringComparison.OrdinalIgnoreCase)) continue;
                    var tool = Combine(value);
                    if (tool != null) return tool;
                }
            }
            catch { /* key absent or unreadable */ }
        }

        return Combine(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "GPSoftware", "Directory Opus"));
    }

    private static string? Combine(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory)) return null;
        try
        {
            var tool = Path.Combine(directory, ToolName);
            return File.Exists(tool) ? tool : null;
        }
        catch { return null; }
    }
}
