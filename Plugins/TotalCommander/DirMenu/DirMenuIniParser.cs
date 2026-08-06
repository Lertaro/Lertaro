using System.IO;
using Lertaro.PluginSdk;

namespace Lertaro.Plugins.TotalCommander.DirMenu;

// Parses Total Commander's own Directory Hotlist (Ctrl+D), stored as the [DirMenu] section of
// %APPDATA%\GHISLER\wincmd.ini, into a tree honoring its "-Name"/"--" submenu nesting.
internal static class DirMenuIniParser
{
    private static string IniPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GHISLER", "wincmd.ini");

    public static List<DirMenuNode> Parse()
    {
        var root = new List<DirMenuNode>();
        var entries = ReadSection(IniPath, "DirMenu");
        if (entries.Count == 0) return root;

        var stack = new Stack<List<DirMenuNode>>();
        stack.Push(root);

        for (var n = 1; entries.TryGetValue($"menu{n}", out var label); n++)
        {
            entries.TryGetValue($"cmd{n}", out var cmd);

            if (label == "-")
            {
                stack.Peek().Add(new DirMenuNode { IsSeparator = true });
            }
            else if (label == "--")
            {
                if (stack.Count > 1) stack.Pop();
            }
            else if (label.StartsWith('-') && label.Length > 1)
            {
                var submenu = new DirMenuNode { Label = label[1..], Children = new List<DirMenuNode>() };
                stack.Peek().Add(submenu);
                stack.Push(submenu.Children);
            }
            else
            {
                // Only "cd <realdir>" leaves are kept -- everything else (no cmd, an unrelated command, or
                // a cd target that no longer exists) has no action worth showing here.
                if (cmd == null) continue;
                var trimmed = cmd.TrimStart();
                if (!trimmed.StartsWith("cd ", StringComparison.OrdinalIgnoreCase)) continue;
                var dir = trimmed[3..].Trim().Trim('"');
                if (!Directory.Exists(dir)) continue;

                stack.Peek().Add(new DirMenuNode { Label = label, Path = dir });
            }
        }

        Prune(root);
        return root;
    }

    // A "-Name"/"--" group whose only entries failed to resolve (unrecognized cmd, a "cd" target that no
    // longer exists) would otherwise survive as a dead-end submenu: it shows up next to real entries, but
    // opening it renders nothing at all. Mirrors GetMenuItems' own root-level rule ("hidden entirely when
    // the hotlist has nothing to show") at every nesting level, not just the root.
    private static bool Prune(List<DirMenuNode> nodes)
    {
        var hasContent = false;
        for (var i = nodes.Count - 1; i >= 0; i--)
        {
            var node = nodes[i];
            if (node.IsSeparator) continue;

            if (node.Children != null)
            {
                if (!Prune(node.Children)) { nodes.RemoveAt(i); continue; }
            }

            hasContent = true;
        }

        return hasContent;
    }

    // Case-insensitive key=value pairs from one [Section], keyed by index suffix rather than physical
    // line order -- Total Commander's own Ctrl+D editor can reorder a hotlist's lines while keeping each
    // entry's numeric suffix stable.
    private static Dictionary<string, string> ReadSection(string iniPath, string sectionName)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(iniPath)) return result;

        try
        {
            var inSection = false;
            foreach (var rawLine in File.ReadLines(iniPath))
            {
                var line = rawLine.Trim();
                if (line.Length == 0) continue;

                if (line[0] == '[' && line[^1] == ']')
                {
                    inSection = line.Equals($"[{sectionName}]", StringComparison.OrdinalIgnoreCase);
                    continue;
                }

                if (!inSection) continue;

                var eq = line.IndexOf('=');
                if (eq <= 0) continue;

                result[line[..eq].Trim()] = line[(eq + 1)..];
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"[TotalCommander] Failed to read DirMenu from '{iniPath}': {ex.Message}", LogLevel.Error);
        }

        return result;
    }
}
