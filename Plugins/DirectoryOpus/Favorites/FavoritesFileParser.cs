using System.IO;
using System.Xml.Linq;
using Lertaro.PluginSdk;

namespace Lertaro.Plugins.DirectoryOpus.Favorites;

// Reads Directory Opus's own Favorites menu, which it keeps as plain XML on disk rather than behind any
// API, so nothing here has to talk to a running Opus:
//
//     %APPDATA%\GPSoftware\Directory Opus\ConfigFiles\favorites.ofv
//
//     <favorites>
//       <folder label="Work">            a submenu, nestable
//         <separator heading="Docs" />
//         <path label="Specs">           a favorited location; label is optional
//           <dir><pathstring>D:\Specs</pathstring><pidl>...</pidl></dir>
//         </path>
//         <path>
//           <file><pathstring>D:\notes.txt</pathstring></file>
//         </path>
//       </folder>
//     </favorites>
//
// Confirmed against a live install rather than assumed: the sample above is the shape of a real
// favorites.ofv, including a <folder> with no label at all and a <path> whose child is <file>.
internal static class FavoritesFileParser
{
    private static string FavoritesPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "GPSoftware", "Directory Opus", "ConfigFiles", "favorites.ofv");

    public static List<FavoritesNode> Parse()
    {
        var xml = ReadFile(FavoritesPath);
        if (xml == null) return new List<FavoritesNode>();

        var root = ParseXml(xml);
        RemoveMissing(root);
        Prune(root);
        TidyDividers(root);
        return root;
    }

    // Opus rewrites this file whenever the user edits their favorites, and it can be doing so at the
    // moment this reads. Sharing write access means a save in progress produces a read error at worst
    // (handled below, one popup shows no favorites) instead of denying Opus its own save.
    private static string? ReadFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch (Exception ex)
        {
            Logger.Log($"[DirectoryOpus] Failed to read favorites from '{path}': {ex.Message}", LogLevel.Error);
            return null;
        }
    }

    /// <summary>
    /// The tree exactly as the file describes it, with no filesystem access.
    /// </summary>
    /// <remarks>
    /// Kept separate from the existence filtering that follows it so the structural parsing -- the part
    /// with all the format's quirks in it -- can be tested against literal XML instead of against
    /// whatever happens to exist on the machine running the tests.
    /// </remarks>
    internal static List<FavoritesNode> ParseXml(string xml)
    {
        try
        {
            var document = XDocument.Parse(xml);
            return document.Root == null ? new List<FavoritesNode>() : ParseChildren(document.Root);
        }
        catch (Exception ex)
        {
            Logger.Log($"[DirectoryOpus] favorites.ofv is not readable XML: {ex.Message}", LogLevel.Error);
            return new List<FavoritesNode>();
        }
    }

    private static List<FavoritesNode> ParseChildren(XElement parent)
    {
        var nodes = new List<FavoritesNode>();

        foreach (var element in parent.Elements())
        {
            switch (element.Name.LocalName)
            {
                // Opus's "Separator", "Gap" and "Heading" entries are all this one element, told apart by
                // their attributes. A gap is blank vertical space in Opus's own favorites BAR, which has
                // room for it; a menu does not, and reproducing it as a drawn line would invent a divider
                // the user never asked for. Dropped. A heading keeps its text, which the provider renders
                // as a titled section row; anything else is a plain divider.
                case "separator":
                    if ((string?)element.Attribute("gap") != null) break;

                    nodes.Add(new FavoritesNode
                    {
                        IsSeparator = true,
                        Label = ((string?)element.Attribute("heading"))?.Trim() ?? string.Empty
                    });
                    break;

                case "folder":
                    var label = (string?)element.Attribute("label");

                    // A <folder> with no label is not a submenu -- it cannot be, as there would be nothing
                    // to title it with. Opus uses it as a plain wrapper, and every separator, gap and
                    // heading in a real file is written inside one. Its contents therefore belong at THIS
                    // level; treating it as a submenu instead put each divider inside a nameless
                    // sub-popup, which Prune then deleted outright as a submenu with no real entries in
                    // it. Confirmed against a file containing one of each of Opus's six entry types.
                    if (string.IsNullOrWhiteSpace(label))
                    {
                        nodes.AddRange(ParseChildren(element));
                        break;
                    }

                    nodes.Add(new FavoritesNode { Label = label, Children = ParseChildren(element) });
                    break;

                case "path":
                    var node = ParsePath(element);
                    if (node != null) nodes.Add(node);
                    break;
            }
        }

        return nodes;
    }

    private static FavoritesNode? ParsePath(XElement element)
    {
        // <dir> and <file> are the only two targets; anything else is a shape this does not know how to
        // navigate to and is better left out than guessed at.
        var target = element.Element("dir") ?? element.Element("file");
        if (target == null) return null;

        var path = ((string?)target.Element("pathstring"))?.Trim();

        // A favorite can carry only a <pidl> -- that is how Opus stores virtual locations like This PC or
        // the Recycle Bin, which have no filesystem path to hand to anything downstream. Skipped rather
        // than shown as an entry that cannot go anywhere. Entries that are merely not rooted (a relative
        // or malformed string) go the same way.
        if (string.IsNullOrEmpty(path) || !Path.IsPathRooted(path)) return null;

        return new FavoritesNode
        {
            Label = LabelFor(element, path),
            Path = path,
            IsFile = target.Name.LocalName == "file"
        };
    }

    // The label attribute is optional, and Opus falls back to showing the location itself when it is
    // missing. The last path segment reads better in a menu than the whole path, but a drive root has no
    // segment to fall back to, so that keeps the full string.
    private static string LabelFor(XElement element, string path)
    {
        var label = (string?)element.Attribute("label");
        if (!string.IsNullOrWhiteSpace(label)) return label;

        var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return string.IsNullOrEmpty(name) ? path : name;
    }

    // A favorite pointing at something that has since been deleted or unmounted would otherwise sit in the
    // menu doing nothing when clicked. Same rule Total Commander's hotlist parser applies to its own "cd"
    // targets.
    private static void RemoveMissing(List<FavoritesNode> nodes)
    {
        for (var i = nodes.Count - 1; i >= 0; i--)
        {
            var node = nodes[i];

            if (node.Children != null)
            {
                RemoveMissing(node.Children);
                continue;
            }

            if (node.Path == null) continue;

            var exists = node.IsFile ? File.Exists(node.Path) : Directory.Exists(node.Path);
            if (!exists) nodes.RemoveAt(i);
        }
    }

    /// <summary>
    /// Drops submenus with nothing left in them, and separators left stranded by that.
    /// </summary>
    /// <remarks>
    /// A folder whose entries all failed to resolve would otherwise survive as a dead end: it sits next to
    /// real entries and opens onto nothing. Applied at every level, not just the root -- the same rule the
    /// root itself follows by being hidden when there is nothing to show.
    /// </remarks>
    internal static bool Prune(List<FavoritesNode> nodes)
    {
        var hasContent = false;

        for (var i = nodes.Count - 1; i >= 0; i--)
        {
            var node = nodes[i];
            if (node.IsSeparator) continue;

            if (node.Children != null && !Prune(node.Children))
            {
                nodes.RemoveAt(i);
                continue;
            }

            hasContent = true;
        }

        if (!hasContent) nodes.Clear();
        return hasContent;
    }

    /// <summary>
    /// Removes plain dividers that no longer divide anything.
    /// </summary>
    /// <remarks>
    /// Runs last, because the two steps before it are what create the problem: unwrapping Opus's
    /// unlabelled &lt;folder&gt; containers lifts every divider up to sit among the real entries, and
    /// dropping favorites that no longer exist can leave a run of them with nothing in between. A real
    /// file that looked orderly in Opus then shows up here opening on a divider, or with two in a row.
    ///
    /// Headings are left alone at every position. Unlike a plain divider, a heading says something on its
    /// own, and one at the very top of a menu is a normal way to title the group beneath it.
    /// </remarks>
    internal static void TidyDividers(List<FavoritesNode> nodes)
    {
        foreach (var node in nodes)
        {
            if (node.Children != null) TidyDividers(node.Children);
        }

        static bool IsPlainDivider(FavoritesNode node) => node.IsSeparator && node.Label.Length == 0;

        for (var i = nodes.Count - 1; i > 0; i--)
        {
            if (IsPlainDivider(nodes[i]) && nodes[i - 1].IsSeparator)
                nodes.RemoveAt(i);
        }

        while (nodes.Count > 0 && IsPlainDivider(nodes[0]))
            nodes.RemoveAt(0);

        while (nodes.Count > 0 && IsPlainDivider(nodes[^1]))
            nodes.RemoveAt(nodes.Count - 1);
    }
}
