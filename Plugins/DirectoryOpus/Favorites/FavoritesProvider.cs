using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Abstractions.Plugins.WindowAdapters;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.DirectoryOpus.Favorites;

// Exposes Directory Opus's own Favorites menu as a Quick Navigation cascade. Same structure and
// conventions as TotalCommander's DirMenuProvider, including the private _nodeMap/_commandMap field names,
// which App/Services/ShellMenu/QuickNavigationPathResolver.cs finds by reflection to resolve a handle back
// to a path for icon loading.
public class FavoritesProvider : IQuickNavigationProvider
{
    private readonly ConcurrentDictionary<IntPtr, object> _nodeMap = new();
    private readonly ConcurrentDictionary<uint, string> _commandMap = new();
    private int _nextId;
    private int _nextCmdId;

    // Matches DirectoryOpusPlugin.Name exactly (a product name, not localized).
    public string GroupName => "Directory Opus";

    // No IQuickNavigationTriggerGate of its own: DirectoryOpusInlineSearchAdapter.CanShowQuickNav already
    // decides when the popup opens over an Opus window, and this provider only contributes content once
    // that has fired.
    public bool CanProvide(ISearchResult result) => result != null;

    public IEnumerable<DynamicMenuItem> GetMenuItems(ISearchResult result, IntPtr hMenu)
    {
        // A single named, iconed root entry rather than dumping the favorites' own top-level entries into
        // the shared popup's root. Hidden entirely when there is nothing to show -- no favorites.ofv, or
        // every entry filtered out -- instead of offering an entry that opens onto an empty submenu.
        if (hMenu == IntPtr.Zero)
        {
            var parsed = FavoritesFileParser.Parse();
            if (parsed.Count == 0) return Enumerable.Empty<DynamicMenuItem>();

            return new[]
            {
                new DynamicMenuItem
                {
                    Text = TranslationService.Get("Plugins_DirectoryOpus_Favorites_RootLabel"),
                    HasSubMenu = true,
                    SubMenuHandle = AllocateHandle(new FavoritesNode { Children = parsed }),
                    HBitmapItem = FavoritesIcon.GetRootHBitmap()
                }
            };
        }

        if (!_nodeMap.TryGetValue(hMenu, out var value) || value is not FavoritesNode node)
            return Enumerable.Empty<DynamicMenuItem>();

        if (node.Children != null)
            return BuildItems(node.Children);

        if (node.Path is string path && Directory.Exists(path))
            return BuildRealFolderItems(path);

        return Enumerable.Empty<DynamicMenuItem>();
    }

    private IEnumerable<DynamicMenuItem> BuildItems(List<FavoritesNode> nodes)
    {
        foreach (var node in nodes)
        {
            if (node.IsSeparator)
            {
                // Opus's "Heading" entry is a separator carrying text. IsHeader renders it the way Opus
                // does -- a non-clickable section title -- rather than throwing the text away and drawing
                // a bare line.
                yield return node.Label.Length > 0
                    ? new DynamicMenuItem { IsHeader = true, Text = node.Label }
                    : new DynamicMenuItem { IsSeparator = true };
                continue;
            }

            // A favorited FILE is a leaf that runs when clicked -- there is nothing to cascade into. This
            // is the one place this differs from the Total Commander hotlist, whose entries are always
            // directories.
            if (node.IsFile)
            {
                yield return new DynamicMenuItem
                {
                    Text = node.Label,
                    CommandId = AllocateCommand(node.Path!)
                };
                continue;
            }

            // Everything else is submenu-only: a <folder> drills into its own children, a favorited
            // directory drills into the real filesystem. Clicking never navigates directly, matching
            // FolderCascader's directory convention. A <folder> is not a real path so it gets the generic
            // group icon; a favorited directory gets none here, because the host resolves
            // FavoritesNode.Path by reflection and loads its real shell icon instead.
            yield return new DynamicMenuItem
            {
                Text = node.Label,
                HasSubMenu = true,
                SubMenuHandle = AllocateHandle(node),
                HBitmapItem = node.Children != null ? FavoritesIcon.GetMenuGroupHBitmap() : IntPtr.Zero,
                // A <folder> has no target of its own; a favorited directory does.
                IsActionable = node.Children == null
            };
        }
    }

    // Mirrors FolderCascader.Navigation.MenuBuilder's real-directory browse (same hidden/system filtering,
    // same directories-cascade/files-execute split) -- once a favorite resolves to a real directory, what
    // it shows next is that directory's actual, current contents, not anything from the file.
    private IEnumerable<DynamicMenuItem> BuildRealFolderItems(string path)
    {
        var items = new List<DynamicMenuItem>();
        try
        {
            var subDirs = Directory.GetDirectories(path)
                .Where(IsVisible)
                .OrderBy(d => d, StringComparer.OrdinalIgnoreCase);
            var subFiles = Directory.GetFiles(path)
                .Where(IsVisible)
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);

            foreach (var dir in subDirs)
            {
                items.Add(new DynamicMenuItem
                {
                    Text = Path.GetFileName(dir),
                    HasSubMenu = true,
                    SubMenuHandle = AllocateHandle(new FavoritesNode { Path = dir })
                });
            }
            foreach (var file in subFiles)
            {
                items.Add(new DynamicMenuItem { Text = Path.GetFileName(file), CommandId = AllocateCommand(file) });
            }
        }
        catch
        {
            // Directory became unreadable between the parent listing and this expansion; fall through to
            // the empty placeholder below instead of leaving the submenu stuck on "Loading...".
        }

        if (items.Count == 0)
        {
            items.Add(new DynamicMenuItem
            {
                Text = TranslationService.Get("Plugins_DirectoryOpus_Favorites_Empty"),
                IsDisabled = true
            });
        }

        return items;
    }

    private static bool IsVisible(string path)
    {
        try { return (File.GetAttributes(path) & (FileAttributes.Hidden | FileAttributes.System)) == 0; }
        catch { return false; }
    }

    public void ExecuteCommand(ISearchResult result, uint commandId, IntPtr ownerHwnd)
    {
        if (!_commandMap.TryGetValue(commandId, out var path)) return;
        try
        {
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            PluginSdk.Logger.Log($"[DirectoryOpus] Favorites failed to open '{path}': {ex.Message}", PluginSdk.LogLevel.Error);
        }
    }

    public void ClearSession()
    {
        _nodeMap.Clear();
        _commandMap.Clear();
        _nextId = 0;
        _nextCmdId = 0;
    }

    private IntPtr AllocateHandle(FavoritesNode node)
    {
        var handle = new IntPtr(Interlocked.Increment(ref _nextId));
        _nodeMap[handle] = node;
        return handle;
    }

    private uint AllocateCommand(string path)
    {
        var cmdId = (uint)Interlocked.Increment(ref _nextCmdId);
        _commandMap[cmdId] = path;
        return cmdId;
    }
}
