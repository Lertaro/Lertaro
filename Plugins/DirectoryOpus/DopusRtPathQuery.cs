using System.Diagnostics;
using System.Globalization;
using System.IO;
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
/// carries both honestly in its attributes (<c>lister</c>, <c>side</c>, <c>active_tab</c>).
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

        var output = Path.Combine(Path.GetTempPath(), $"lertaro-dopus-paths-{Environment.ProcessId}.xml");
        try
        {
            RunTool(tool, output);
            if (!File.Exists(output)) return null;

            // Opus answers an empty <results .../> when nothing is open, which is a valid "no tabs".
            return OrderTabs(ParseTabs(File.ReadAllText(output)));
        }
        catch (Exception ex)
        {
            Logger.Log($"[DirectoryOpus] dopusrt paths query failed: {ex.Message}", LogLevel.Debug);
            return null;
        }
        finally
        {
            try { if (File.Exists(output)) File.Delete(output); } catch { /* temp file */ }
        }
    }

    private static void RunTool(string tool, string output)
    {
        var startInfo = new ProcessStartInfo(tool)
        {
            // Opus's documented form: /info <output file>,<command>
            Arguments = $"/info \"{output}\",paths",
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
    /// than by a live Opus installation: a tab is identified by <c>lister</c> + <c>side</c>, and the
    /// active one of each group is the entry carrying <c>active_tab</c>.
    /// </summary>
    internal static IReadOnlyList<DopusTab> ParseTabs(string xml)
    {
        var tabs = new List<DopusTab>();
        if (string.IsNullOrWhiteSpace(xml)) return tabs;

        var document = XDocument.Parse(xml);
        foreach (var element in document.Descendants("path"))
        {
            var path = ResolvePath(element.Attribute("display_path")?.Value ?? element.Value);
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
