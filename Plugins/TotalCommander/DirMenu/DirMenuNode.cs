namespace Lertaro.Plugins.TotalCommander.DirMenu;

// Covers both statically-defined ini entries (Children set, from a "-Name"/"--" submenu group) and
// dynamically-discovered real subfolders (Path set, found while browsing a resolved directory). Files
// found during that browse are leaf DynamicMenuItems directly, so they never need a node of their own.
//
// Path must be a property, not a field: App/Services/ShellMenu/QuickNavigationPathResolver.cs resolves a
// submenu handle back to a path via reflection (GetProperty("Path")) to load a real file-type icon --
// GetProperty finds compiled property accessors only, never a plain field, so a field here would silently
// leave every cascaded directory entry iconless.
internal sealed class DirMenuNode
{
    public string Label { get; set; } = "";
    public bool IsSeparator { get; set; }
    public string? Path { get; set; }
    public List<DirMenuNode>? Children { get; set; }
}
