namespace Lertaro.Core;

/// <summary>What a quick panel group orders its items by.</summary>
public enum QuickPanelSortMode
{
    ModifiedDescending,
    NameAscending,
}

/// <summary>Where a folder source's items come from, and in what order.</summary>
public enum QuickPanelSourceKind
{
    /// <summary>Only what changed recently, newest first -- answered from the index, not by walking.</summary>
    RecentFiles,
    /// <summary>
    /// Everything in the folder, newest first. Distinct from RecentFiles, which drops anything older
    /// than its age limit: this one never hides a file, it only decides what to show first.
    /// </summary>
    AllByModified,
    /// <summary>All files and folders in the directory.</summary>
    All,
    /// <summary>Only subdirectories / folders.</summary>
    FoldersOnly,
    /// <summary>Only files.</summary>
    FilesOnly,
}

/// <summary>
/// Backs the quick panel: the floating panel docked over whatever window is in front. Each tab is a
/// workspace -- its own sources, its own order, its own display preferences -- and the panel shows one
/// tab at a time.
/// </summary>
public class QuickPanelSettings
{
    public bool Enabled { get; set; } = true;

    public List<QuickPanelTab> Tabs { get; set; } = new() { QuickPanelTab.CreateDefault() };

    /// <summary>The tab the panel reopens on. Falls back to the first tab when it no longer exists.</summary>
    public string ActiveTabId { get; set; } = string.Empty;

    /// <summary>
    /// Plugin-provided tabs the user has closed, by component id. Present unless closed, which is the
    /// opposite of how a folder source works and is deliberate: a folder is something the user went and
    /// added, while a plugin tab is something a plugin they installed already offers.
    /// </summary>
    public List<string> ClosedPluginTabIds { get; set; } = new();

    /// <summary>
    /// Plugin tabs shown as a detail list rather than as thumbnail tiles, by component id. Listed rather
    /// than a flag per tab for the same reason the closed ones are: a plugin tab has no settings object
    /// of its own to carry one, and the id is what everything else about it is keyed by.
    /// </summary>
    public List<string> ListViewPluginTabIds { get; set; } = new();

    /// <summary>
    /// The strip's order, left to right, over workspaces and plugin tabs at once. An id that isn't listed
    /// (a workspace just created, a plugin that just appeared) keeps its discovery-order position after
    /// everything listed -- the same rule <see cref="QuickPanelTab.GroupOrder"/> follows for groups.
    /// </summary>
    /// <remarks>
    /// A single list over both kinds, rather than ordering the workspaces by their own position in
    /// <see cref="Tabs"/> and the plugin tabs by something else: they share one strip and one set of
    /// number keys, so any arrangement that cannot interleave them is a rule the user would have to be
    /// told about.
    /// </remarks>
    public List<string> TabOrder { get; set; } = new();

    /// <summary>
    /// Further applications the panel refuses to open over, by process name ("chrome" or "chrome.exe",
    /// either way). Added ON TOP of <see cref="UserSettings.BlacklistedProcesses"/>, never instead of
    /// it: whatever is blocked globally is blocked here too. This list exists for the apps that only
    /// this panel has a reason to avoid -- it docks itself onto the window in front, so a full-screen
    /// player or a game is ruined by it without deserving a global block.
    /// </summary>
    public List<string> BlacklistedProcesses { get; set; } = new();
}

/// <summary>One workspace, shown as one tab in the panel's tab strip.</summary>
public class QuickPanelTab
{
    /// <summary>Stable identity, so renaming a tab keeps its group preferences.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Empty falls back to a translated default, so a never-renamed tab follows the UI language.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Off keeps the workspace configured but out of the way: no tab for it in the panel, and no
    /// process of its claims it. For the workspace you set up for a project you are not on this month,
    /// where deleting it means rebuilding the source list to get it back.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Applications this workspace belongs to, by process name. Summon the panel while one of them is
    /// in front and it opens on this tab instead of wherever it was left -- which is the whole point of
    /// having workspaces docked onto the window in front: the app you are in already says which set of
    /// folders you mean. Empty means the tab is only ever reached by hand.
    /// </summary>
    public List<string> Processes { get; set; } = new();

    public List<QuickPanelFolderSource> Folders { get; set; } = new();

    /// <summary>
    /// Display order, most-preferred first, over this workspace's folders. An id that isn't listed (a
    /// folder just added) keeps its discovery-order position after everything listed.
    /// </summary>
    /// <remarks>
    /// Plugin-provided lists are not in here, and were briefly: they were added to a workspace the way a
    /// folder is, which meant Favorites had to be ticked into every workspace separately and vanished
    /// from any new one. They are not part of any workspace -- they are whole collections, orthogonal to
    /// whichever set of folders you are looking at -- so they are tabs of their own now, and their order
    /// lives in <see cref="QuickPanelSettings.TabOrder"/> alongside the workspaces'.
    /// </remarks>
    public List<string> GroupOrder { get; set; } = new();

    /// <summary>
    /// Sources hidden from this tab. A single list rather than an Enabled flag per source: the built-in
    /// and (later) plugin sources have no settings object of their own to carry one, and splitting the
    /// two would mean asking two different questions to learn whether one group is shown.
    /// </summary>
    public List<string> DisabledGroupIds { get; set; } = new();

    public Dictionary<string, QuickPanelGroupPreference> GroupPreferences { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The tab a fresh install starts with: the three folders most people keep things in, as recent
    /// files. Neither built-in source is included -- both are one button away, and a panel that opens
    /// with someone's favorites and every document Windows has ever logged is guessing at what they
    /// wanted rather than showing what they asked for.
    /// </summary>
    public static QuickPanelTab CreateDefault() => new()
    {
        Id = NewId(),
        Folders =
        {
            QuickPanelFolderSource.For("shell:Desktop"),
            QuickPanelFolderSource.For("shell:Downloads"),
            QuickPanelFolderSource.For("shell:Personal"),
        },
    };

    /// <summary>Short, stable, and never shown -- only ever compared.</summary>
    public static string NewId() => Guid.NewGuid().ToString("N").Substring(0, 8);

    /// <summary>
    /// A copy that shares nothing mutable with this one, ids included. What the settings page edits, so
    /// that adding a folder or deleting a workspace is staged like every other setting rather than
    /// taking effect the moment it is typed -- these objects live inside the process-wide UserSettings,
    /// and mutating them in place is both instantly live and unaffected by Cancel.
    /// </summary>
    public QuickPanelTab Clone() => new()
    {
        Id = Id,
        Name = Name,
        Enabled = Enabled,
        Processes = new List<string>(Processes),
        Folders = Folders.Select(f => new QuickPanelFolderSource
        {
            Id = f.Id,
            Path = f.Path,
            Kind = f.Kind,
            SortByModified = f.SortByModified,
            Recursive = f.Recursive,
            FilterPattern = f.FilterPattern,
            MaxItems = f.MaxItems,
            MaxAgeMinutes = f.MaxAgeMinutes,
            AcceptsDrops = f.AcceptsDrops,
        }).ToList(),
        GroupOrder = new List<string>(GroupOrder),
        DisabledGroupIds = new List<string>(DisabledGroupIds),
        GroupPreferences = GroupPreferences.ToDictionary(
            pair => pair.Key,
            pair => new QuickPanelGroupPreference
            {
                DisplayName = pair.Value.DisplayName,
                Sort = pair.Value.Sort,
                ThumbnailView = pair.Value.ThumbnailView,
                Expanded = pair.Value.Expanded,
            },
            StringComparer.OrdinalIgnoreCase),
    };
}

/// <summary>A folder the user added as a source. One folder is one group.</summary>
public class QuickPanelFolderSource
{
    /// <summary>
    /// Its identity for order/visibility/preferences, generated once. Not the path: editing the path of
    /// a source you already renamed and reordered should keep it the same source.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;

    public QuickPanelSourceKind Kind { get; set; } = QuickPanelSourceKind.RecentFiles;

    public bool SortByModified { get; set; }

    public bool Recursive { get; set; }

    /// <summary>One or more Win32 file patterns separated by ';' or ','; empty means no filtering.</summary>
    public string FilterPattern { get; set; } = string.Empty;

    /// <summary>How many entries this group shows at most. 0 means everything the source has.</summary>
    public int MaxItems { get; set; } = 20;

    /// <summary>Only entries changed within this many minutes qualify. 0 means no age limit at all.</summary>
    public int MaxAgeMinutes { get; set; }

    /// <summary>
    /// Whether this source's group is a drop target: files dragged onto it are copied into this folder.
    /// Off by default, and asked per source rather than once for the panel -- a folder kept as an inbox
    /// wants this, and a "recent files" source pointing somewhere you only ever read from does not, and
    /// there is no way to tell those apart except by asking.
    /// </summary>
    public bool AcceptsDrops { get; set; }

    public static QuickPanelFolderSource For(string path, QuickPanelSourceKind kind = QuickPanelSourceKind.RecentFiles)
        => new() { Id = QuickPanelTab.NewId(), Path = path, Kind = kind };

    /// <summary>
    /// What the group is called when the user has not renamed it: the folder's own name, or the path
    /// itself for a drive root, which has no last segment. Here rather than on either of the two view
    /// models that need it -- the settings row and the panel's group heading have to agree on this, and
    /// a rename in one place is only visible as a mismatch in the other.
    /// </summary>
    public static string DefaultName(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;
        // Spelled out: this type's own Path property shadows System.IO.Path inside it.
        var trimmed = path.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
        var leaf = System.IO.Path.GetFileName(trimmed);
        return string.IsNullOrEmpty(leaf) ? path : leaf;
    }
}

/// <summary>
/// How one group is displayed, stored per source id so it applies to folder sources, the built-in ones
/// and (later) plugin sources alike. What the user does to a group in the panel itself -- reorder it,
/// rename it, collapse it, switch it to a list -- lands here and survives the panel closing.
/// </summary>
public class QuickPanelGroupPreference
{
    /// <summary>
    /// Overrides the group heading. Empty means the source's own default name, which for the built-in
    /// sources is translated and therefore follows the UI language -- a name typed here does not.
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    public QuickPanelSortMode Sort { get; set; } = QuickPanelSortMode.ModifiedDescending;

    /// <summary>
    /// The order a source starts in when the user has never overridden it.
    /// </summary>
    public static QuickPanelSortMode DefaultSortFor(QuickPanelSourceKind kind)
        => (kind == QuickPanelSourceKind.AllByModified || kind == QuickPanelSourceKind.RecentFiles)
            ? QuickPanelSortMode.ModifiedDescending
            : QuickPanelSortMode.NameAscending;

    public static QuickPanelSortMode DefaultSortFor(QuickPanelFolderSource source)
        => (source.SortByModified || source.Kind == QuickPanelSourceKind.AllByModified || source.Kind == QuickPanelSourceKind.RecentFiles)
            ? QuickPanelSortMode.ModifiedDescending
            : QuickPanelSortMode.NameAscending;

    public bool ThumbnailView { get; set; } = true;

    public bool Expanded { get; set; } = true;
}
