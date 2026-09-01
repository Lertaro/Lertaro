using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Services;

using Lertaro.PluginSdk.Abstractions.Plugins.WindowAdapters;
namespace Lertaro.Plugins.TotalCommander.DirMenu;

// Exposes Total Commander's own Directory Hotlist ([DirMenu] in wincmd.ini) as a Quick Navigation cascade.
// Mirrors Plugins/FolderCascader/Navigation/Provider.cs's structure and conventions exactly (including the
// private _nodeMap/_commandMap field names, which App/Services/ShellMenu/QuickNavigationPathResolver.cs
// finds by reflection to resolve a handle back to a path for icon loading).
public class DirMenuProvider : IQuickNavigationProvider
{
    private readonly ConcurrentDictionary<IntPtr, object> _nodeMap = new();
    private readonly ConcurrentDictionary<uint, string> _commandMap = new();
    private int _nextId;
    private int _nextCmdId;

    // Matches TotalCommanderPlugin.Name exactly (a product name, not localized).
    public string GroupName => "Total Commander";

    // No IQuickNavigationTriggerGate of its own: FolderCascader's own trigger already covers Total
    // Commander's file list generically via TotalCommanderInlineSearchAdapter.CanShowQuickNav, and this
    // provider only ever contributes content once that has already fired.
    public bool CanProvide(ISearchResult result) => result != null;

    public IEnumerable<DynamicMenuItem> GetMenuItems(ISearchResult result, IntPtr hMenu)
    {
        // A single named, iconed root entry -- like FolderCascader's own "Favorites"/"History" categories
        // -- rather than dumping the hotlist's own top-level entries straight into the shared popup's root.
        // Hidden entirely when the hotlist has nothing to show (no [DirMenu] section, or every entry got
        // filtered out), rather than offering an entry that only ever opens onto an empty submenu. Labeled
        // by the actual feature name (matching the docs' own term for it), not "Total Commander" again --
        // the quick-nav group header above this entry already says that, from GroupName.
        if (hMenu == IntPtr.Zero)
        {
            var parsed = DirMenuIniParser.Parse();
            if (parsed.Count == 0) return Enumerable.Empty<DynamicMenuItem>();

            return new[]
            {
                new DynamicMenuItem
                {
                    Text = TranslationService.Get("Plugins_TotalCommander_DirMenu_RootLabel"),
                    HasSubMenu = true,
                    SubMenuHandle = AllocateHandle(new DirMenuNode { Children = parsed }),
                    HBitmapItem = DirMenuIcon.GetRootHBitmap()
                }
            };
        }

        if (!_nodeMap.TryGetValue(hMenu, out var value) || value is not DirMenuNode node)
            return Enumerable.Empty<DynamicMenuItem>();

        if (node.Children != null)
            return BuildItems(node.Children);

        if (node.Path is string path && Directory.Exists(path))
            return BuildRealFolderItems(path);

        return Enumerable.Empty<DynamicMenuItem>();
    }

    private IEnumerable<DynamicMenuItem> BuildItems(List<DirMenuNode> nodes)
    {
        foreach (var node in nodes)
        {
            if (node.IsSeparator)
            {
                yield return new DynamicMenuItem { IsSeparator = true };
                continue;
            }

            // Both a static ini submenu group and a resolved "cd <realdir>" leaf are submenu-only here:
            // clicking never navigates directly (matching FolderCascader's own directory convention), it
            // always drills one level deeper -- into the ini's own children, or into the real filesystem.
            // A submenu group isn't a real path, so it gets a generic folder icon; a resolved directory
            // leaf gets none here -- the host resolves DirMenuNode.Path by reflection and loads its real
            // shell icon instead.
            yield return new DynamicMenuItem
            {
                Text = node.Label,
                HasSubMenu = true,
                SubMenuHandle = AllocateHandle(node),
                HBitmapItem = node.Children != null ? DirMenuIcon.GetMenuGroupHBitmap() : IntPtr.Zero,
                // A submenu group has no real target of its own -- only a resolved "cd <realdir>" leaf does.
                IsActionable = node.Children == null
            };
        }
    }

    // Mirrors FolderCascader.Navigation.MenuBuilder's real-directory browse (same hidden/system filtering,
    // same directories-cascade/files-execute split) -- once a DirMenu entry resolves to a real directory,
    // what it shows next is that directory's actual, current contents, not anything from the ini.
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
                    SubMenuHandle = AllocateHandle(new DirMenuNode { Path = dir })
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
                Text = TranslationService.Get("Plugins_TotalCommander_DirMenu_Empty"),
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
            PluginSdk.Logger.Log($"[TotalCommander] DirMenu failed to open '{path}': {ex.Message}", PluginSdk.LogLevel.Error);
        }
    }

    public void ClearSession()
    {
        _nodeMap.Clear();
        _commandMap.Clear();
        _nextId = 0;
        _nextCmdId = 0;
    }

    private IntPtr AllocateHandle(DirMenuNode node)
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
