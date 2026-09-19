using System.Globalization;
using System.IO;
using System.Xml.Linq;
using Lertaro.PluginSdk.Abstractions.Plugins.WindowAdapters;
using Lertaro.PluginSdk.Helpers;

namespace Lertaro.Plugins.DirectoryOpus;

/// <summary>One tab Directory Opus reports: which lister and side it belongs to, and its folder.</summary>
/// <param name="Lister">The lister's window handle, as reported by Opus.</param>
/// <param name="Side">1 or 2 -- the two tab groups a lister shows side by side. Groups are per lister+side.</param>
/// <param name="Path">The folder the tab is showing.</param>
/// <param name="IsActive">Whether this is the tab the user is currently looking at in its group.</param>
internal readonly record struct DopusTab(IntPtr Lister, int Side, string Path, bool IsActive);

/// <summary>
/// The shape of the answer to <c>dopusrt.exe /info &lt;file&gt;,paths</c> -- the folder in each
/// <c>&lt;path&gt;</c> element's own text, and the grouping in its attributes (<c>lister</c>,
/// <c>side</c>, <c>active_tab</c>) -- turned into the tab list and the opened-folder order the rest of
/// the plugin consumes.
/// </summary>
/// <remarks>
/// Split out of <see cref="DopusRtPathQuery"/> purely to keep that file under the repo's per-file line
/// limit; nothing here starts a process, reads a file or touches a window, which is also what lets the
/// attribute handling and the ordering be pinned by tests instead of by a live Opus installation.
/// </remarks>
internal static class DopusPathsXml
{
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
    public static IReadOnlyList<OpenedFolder> ToOpenedFolders(IReadOnlyList<DopusTab> tabs)
    {
        var folders = new List<OpenedFolder>(tabs.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var tab in tabs)
        {
            // Keyed per lister and with the trailing separator dropped: Directory Opus reports a drive
            // root with one and everything else without, and a folder reached both ways is one folder.
            var key = tab.Lister.ToInt64().ToString(CultureInfo.InvariantCulture) + "|" +
                tab.Path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (seen.Add(key))
                folders.Add(new OpenedFolder(tab.Path, tab.Lister));
        }

        return folders;
    }
}
