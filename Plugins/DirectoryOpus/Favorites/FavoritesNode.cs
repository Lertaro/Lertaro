namespace Lertaro.Plugins.DirectoryOpus.Favorites;

// One entry of Directory Opus's Favorites menu. Covers all three things favorites.ofv can hold: a
// submenu (<folder>, Children set), a favorited location (<path>, Path set), and a divider
// (<separator>). Also covers the real subfolders discovered while browsing a favorited directory, which
// are the same shape -- a label and a path -- so they need no type of their own.
//
// Path must be a property, not a field: App/Services/ShellMenu/QuickNavigationPathResolver.cs resolves a
// submenu handle back to a path via reflection (GetProperty("Path")) to load a real file-type icon --
// GetProperty finds compiled property accessors only, never a plain field, so a field here would silently
// leave every cascaded directory entry iconless. Same reason DirMenuNode states it.
internal sealed class FavoritesNode
{
    /// <summary>
    /// Display text. On a separator this is its optional heading, which Opus renders as a titled section
    /// break rather than a plain divider; empty means a plain divider.
    /// </summary>
    public string Label { get; set; } = "";

    public bool IsSeparator { get; set; }
    public string? Path { get; set; }

    /// <summary>
    /// True when <see cref="Path"/> points at a file rather than a directory.
    /// </summary>
    /// <remarks>
    /// Unlike Total Commander's hotlist, which only holds "cd &lt;dir&gt;" entries, Directory Opus lets a
    /// file be favorited outright (&lt;file&gt; instead of &lt;dir&gt; in favorites.ofv). The two behave
    /// differently in the menu: a directory opens a submenu of its contents, a file is a leaf that runs
    /// when clicked.
    /// </remarks>
    public bool IsFile { get; set; }

    public List<FavoritesNode>? Children { get; set; }
}
