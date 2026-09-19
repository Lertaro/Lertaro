using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Xml.Linq;
using Lertaro.PluginSdk;
using Lertaro.PluginSdk.Helpers;
using Microsoft.Win32;

namespace Lertaro.Plugins.DirectoryOpus;

/// <summary>One tab Directory Opus reports: which lister and side it belongs to, and its folder.</summary>
/// <param name="Lister">The lister's window handle, as reported by Opus.</param>
/// <param name="Side">1 or 2 -- the two tab groups a lister shows side by side. Groups are per lister+side.</param>
/// <param name="Path">The folder the tab is showing.</param>
/// <param name="IsActive">Whether this is the tab the user is currently looking at in its group.</param>
internal readonly record struct DopusTab(IntPtr Lister, int Side, string Path, bool IsActive);

/// <summary>
/// Reads the folders Opus is showing through its own documented interface --
/// <c>dopusrt.exe /info &lt;file&gt;,paths</c>, which Opus documents as "a list of paths currently
/// displayed in all tabs in all Listers" -- instead of scraping the file-display windows for their text.
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
        var tool = FindTool();
        if (tool == null) return null;

        var output = CreateOutputPath();
        if (output == null)
        {
            // Opus's own rules for this path (ASCII, no spaces, an existing directory, a file nobody else
            // holds) are not satisfiable here, so the query cannot be made safely at all. The scrape
            // fallback is the honest answer rather than handing Opus a path it will refuse.
            Logger.Log("[DirectoryOpus] no ASCII, space-free temp path for the dopusrt query; skipping it.", LogLevel.Debug);
            return null;
        }

        try
        {
            RunTool(tool, output);

            // Success is the FILE, never the exit code: dopusrt answers 0 with no error and no message for
            // a call that worked, a command it did not understand and a path it refused alike, so the only
            // evidence is an output file that appeared and holds something. The name is unique to this
            // call, so a file here was written by THIS Opus and cannot be a leftover from a crashed run.
            var text = WaitForOutput(output);
            if (text == null) return null;

            // Opus answers an empty <results .../> when nothing is open, which is a valid "no tabs".
            return OrderTabs(ParseTabs(text));
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
    /// Waits for dopusrt to finish writing and returns what it wrote, or null when no usable file
    /// appeared. The file's size and write time are the whole verdict -- see the exit-code note above.
    /// </summary>
    private static string? WaitForOutput(string path)
    {
        for (var attempt = 0; attempt < 20; attempt++)
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

            Thread.Sleep(100);
        }

        Logger.Log($"[DirectoryOpus] dopusrt produced no usable output at '{path}' within 2s; its exit code says nothing either way.", LogLevel.Debug);
        return null;
    }

    /// <summary>
    /// A fresh output path for one query, or null when this machine has none Opus can use.
    /// </summary>
    /// <remarks>
    /// Opus requires the path to contain no space, no Chinese and no other special character, to exist,
    /// and to be a file nothing else holds. Hence: a NEW name every call (never a reused one a previous
    /// run could have left behind or a concurrent query could be writing), in a directory validated
    /// against those character rules. %TEMP% is per-user and routinely contains a space or a non-ASCII
    /// user name; its 8.3 short form is the ASCII-only spelling of the same existing directory, which is
    /// what makes this work there. Where 8.3 names are disabled the short form comes back unchanged, the
    /// check below fails, and the caller degrades to the fallback instead of asking Opus to write a path
    /// it would reject.
    /// </remarks>
    internal static string? CreateOutputPath()
    {
        foreach (var directory in new[] { ToShortForm(Path.GetTempPath()), Path.GetTempPath() })
        {
            if (string.IsNullOrEmpty(directory)) continue;

            var candidate = Path.Combine(directory, $"lertaro-dopusrt-{Guid.NewGuid():N}.xml");
            if (IsOpusSafePath(candidate)) return candidate;
        }

        return null;
    }

    /// <summary>
    /// Whether a path satisfies what Opus documents for the output file: ASCII only, with no spaces or
    /// other special characters. Pure, so the rule is pinned by a test rather than by this machine's
    /// temp directory happening to be a friendly one.
    /// </summary>
    internal static bool IsOpusSafePath(string? path)
    {
        if (string.IsNullOrEmpty(path) || path.Contains(' ')) return false;

        foreach (var character in path)
        {
            if (character < 0x20 || character > 0x7E) return false;
        }

        return true;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetShortPathNameW(string longPath, StringBuilder shortPath, uint capacity);

    /// <summary>The 8.3 spelling of a directory (<c>C:\Users\ZHANG~1\...</c>), which is ASCII by construction.</summary>
    private static string? ToShortForm(string path)
    {
        try
        {
            var buffer = new StringBuilder(520);
            var length = GetShortPathNameW(path, buffer, (uint)buffer.Capacity);
            return length > 0 && length < buffer.Capacity ? buffer.ToString().TrimEnd('\\') : null;
        }
        catch { return null; }
    }

    private static void RunTool(string tool, string output)
    {
        var startInfo = new ProcessStartInfo(tool)
        {
            // Opus's documented form is "/info <output file>,<command>", with no quoting of its own -- and
            // the path is validated space-free for exactly that reason (see CreateOutputPath), so quoting
            // it here would only be second-guessing the documented form.
            Arguments = $"/info {output},paths",
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo);
        // Opus writes the file before exiting; without a bound a wedged install would hold the caller --
        // which is the Hook, where a stall costs the whole foreground pipeline.
        if (process != null && !process.WaitForExit(3000))
        {
            try { process.Kill(); } catch { /* best effort */ }
        }
    }

    /// <summary>
    /// Reads the paths out of Opus's XML. Pure, so the attribute handling is pinned by a test rather
    /// than by a live Opus installation: a tab is identified by <c>lister</c> + <c>side</c>, the folder
    /// is the element's own text (see <see cref="ChooseReportedPath"/>), and the active tab of each
    /// group is the entry carrying <c>active_tab</c>.
    /// </summary>
    internal static IReadOnlyList<DopusTab> ParseTabs(string xml)
    {
        var tabs = new List<DopusTab>();
        if (string.IsNullOrWhiteSpace(xml)) return tabs;

        var document = XDocument.Parse(xml);
        foreach (var element in document.Descendants("path"))
        {
            var path = ResolvePath(ChooseReportedPath(element.Value, element.Attribute("display_path")?.Value));
            if (string.IsNullOrEmpty(path)) continue;

            tabs.Add(new DopusTab(
                Lister: ParseHandle(element.Attribute("lister")?.Value),
                Side: int.TryParse(element.Attribute("side")?.Value, out var side) ? side : 0,
                Path: path,
                IsActive: element.Attribute("active_tab") != null));
        }

        return tabs;
    }

    /// <summary>
    /// The order the list should show: the active tab of every group first -- the folders the user is
    /// actually looking at, one per group -- and then each group's remaining tabs in Opus's own order.
    /// Groups are (lister, side) and keep the order Opus reported them in.
    /// </summary>
    internal static IReadOnlyList<DopusTab> OrderTabs(IEnumerable<DopusTab> tabs)
    {
        var all = tabs.ToList();
        var groups = new List<(IntPtr Lister, int Side)>();
        foreach (var tab in all)
        {
            if (!groups.Contains((tab.Lister, tab.Side))) groups.Add((tab.Lister, tab.Side));
        }

        var ordered = new List<DopusTab>(all.Count);
        foreach (var group in groups)
            ordered.AddRange(all.Where(tab => (tab.Lister, tab.Side) == group && tab.IsActive));
        foreach (var group in groups)
            ordered.AddRange(all.Where(tab => (tab.Lister, tab.Side) == group && !tab.IsActive));

        return ordered;
    }

    /// <summary>
    /// The path one <c>&lt;path&gt;</c> element names: its element text, which is the real filesystem
    /// path, falling back to <c>display_path</c> only when the text is missing.
    /// </summary>
    /// <remarks>
    /// The element TEXT is authoritative and <c>display_path</c> is display-only -- Opus localizes it on
    /// a non-English Windows. Measured on a live install, one tab reported
    /// <c>display_path="C:\用户\testuser\AppData\Local\Temp"</c> while its text read
    /// <c>C:\Users\testuser\AppData\Local\Temp</c>; the localized spelling is not a path that exists, and
    /// every such tab was silently unusable. Reading the text instead removes the dependency on the
    /// machine's display language entirely, so there is no lookup table to keep in sync.
    /// Pure, so the choice is pinned by a test rather than by this machine's Windows display language.
    /// </remarks>
    internal static string? ChooseReportedPath(string? elementText, string? displayPath) =>
        !string.IsNullOrWhiteSpace(elementText) ? elementText : displayPath;

    private static string? ResolvePath(string? reported)
    {
        if (string.IsNullOrWhiteSpace(reported)) return null;
        var resolved = ShellPathHelper.ResolveSpecialFolder(reported);
        if (resolved.Length == 2 && resolved[1] == ':') resolved += "\\";
        return string.IsNullOrWhiteSpace(resolved) ? null : resolved;
    }

    // Opus writes these handles as "0x8c0a44", and NumberStyles.HexNumber rejects the prefix outright
    // (it would silently parse every handle as zero and collapse every lister into one group), so the
    // prefix is stripped first.
    private static IntPtr ParseHandle(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return IntPtr.Zero;
        var text = hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? hex[2..] : hex;
        return long.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value) ? new IntPtr(value) : IntPtr.Zero;
    }

    /// <summary>
    /// The ordered tabs as the snapshot's entries: one entry per distinct folder within a lister (two
    /// tabs showing the same folder are one folder to offer, and both entries would carry the same
    /// lister), while the same folder open in two listers stays two entries -- the contract
    /// <c>OpenedFolderCollectorRegistry</c> documents for collectors.
    /// </summary>
    public static IReadOnlyList<PluginSdk.Abstractions.Plugins.WindowAdapters.OpenedFolder> ToOpenedFolders(
        IReadOnlyList<DopusTab> tabs)
    {
        var folders = new List<PluginSdk.Abstractions.Plugins.WindowAdapters.OpenedFolder>(tabs.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var tab in tabs)
        {
            // Keyed per lister and with the trailing separator dropped: Directory Opus reports a drive
            // root with one and everything else without, and a folder reached both ways is one folder.
            var key = tab.Lister.ToInt64().ToString(CultureInfo.InvariantCulture) + "|" +
                tab.Path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (seen.Add(key))
                folders.Add(new PluginSdk.Abstractions.Plugins.WindowAdapters.OpenedFolder(tab.Path, tab.Lister));
        }

        return folders;
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
