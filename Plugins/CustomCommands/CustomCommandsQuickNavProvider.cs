using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Services;

using Lertaro.PluginSdk.Abstractions.Plugins.WindowAdapters;
namespace Lertaro.Plugins.CustomCommands;

// Surfaces commands with ShowInQuickNav=true as root-level entries in the quick navigation cascader.
// Global, not tied to any path/context the way FolderCascader's own provider is -- CanProvide/GetMenuItems
// ignore the ISearchResult they're given entirely.
public class CustomCommandsQuickNavProvider : IQuickNavigationProvider
{
    public string GroupName => TranslationService.Get("CustomCommands_PluginName");

    // Handle -> the submenu path segments already consumed to reach that node (e.g. ["a", "b"] for a
    // submenu opened by drilling into "a" then "b"). Root level is IntPtr.Zero, never allocated here.
    private readonly Dictionary<IntPtr, string[]> _nodeMap = new();
    private int _nextId = 1;

    // Cached per popup session (cleared in ClearSession, called once per Show()) rather than reloaded
    // on every call -- CanProvide, the root GetMenuItems, and every submenu expansion would otherwise
    // each re-deserialize the whole Commands list from settings.
    private List<CustomCommandsInstantProvider.CommandItem>? _cache;

    public bool CanProvide(ISearchResult result) => LoadQuickNavCommands().Count > 0;

    public IEnumerable<DynamicMenuItem> GetMenuItems(ISearchResult result, IntPtr hMenu)
    {
        var commands = LoadQuickNavCommands();
        if (commands.Count == 0) yield break;

        string[]? prefix;
        if (hMenu == IntPtr.Zero)
            prefix = Array.Empty<string>();
        else if (!_nodeMap.TryGetValue(hMenu, out prefix))
            yield break;

        // First-seen order: a leaf whose path ends exactly at this level is yielded immediately; the
        // first command belonging to a not-yet-seen category yields that category's submenu entry (at
        // its own position in the config list), and every later command sharing that same category is
        // skipped here -- its turn comes when that submenu itself gets expanded.
        var seenCategories = new HashSet<string>(StringComparer.Ordinal);
        foreach (var cmd in commands)
        {
            var segments = SplitPath(cmd.SubMenu);
            if (!StartsWithPrefix(segments, prefix!)) continue;

            if (segments.Length == prefix!.Length)
            {
                var capturedCmd = cmd;
                yield return new DynamicMenuItem
                {
                    Text = !string.IsNullOrEmpty(cmd.Title) ? cmd.Title : cmd.Keyword,
                    HBitmapItem = QuickNavIcon.GetCommandHBitmap(cmd.Icon),
                    OnExecute = () => CommandRunner.Run(capturedCmd)
                };
                continue;
            }

            var category = segments[prefix.Length];
            if (!seenCategories.Add(category)) continue;

            var childPrefix = new string[prefix.Length + 1];
            Array.Copy(prefix, childPrefix, prefix.Length);
            childPrefix[prefix.Length] = category;

            yield return new DynamicMenuItem
            {
                Text = category,
                HasSubMenu = true,
                SubMenuHandle = AllocateHandle(childPrefix),
                HBitmapItem = QuickNavIcon.GetCategoryHBitmap(),
                IsActionable = false
            };
        }
    }

    // Never called: every entry executes via its own OnExecute delegate instead (set in GetMenuItems
    // above), same as CustomActions.DynamicActionProvider's own leaf items -- no CommandId needed.
    public void ExecuteCommand(ISearchResult result, uint commandId, IntPtr ownerHwnd) { }

    public void ClearSession()
    {
        _nodeMap.Clear();
        _nextId = 1;
        _cache = null;
        QuickNavIcon.Invalidate();
    }

    private IntPtr AllocateHandle(string[] segments)
    {
        var handle = new IntPtr(_nextId++);
        _nodeMap[handle] = segments;
        return handle;
    }

    // Empty segments (e.g. "a//b", "a/", "/a") are dropped rather than producing an empty-named
    // category or erroring -- a stray typo in the config shouldn't break navigation.
    private static string[] SplitPath(string subMenu) =>
        string.IsNullOrWhiteSpace(subMenu)
            ? Array.Empty<string>()
            : subMenu.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    // Case-sensitive by design (user's own explicit choice): "Tools" and "tools" are two distinct
    // categories, never merged.
    private static bool StartsWithPrefix(string[] segments, string[] prefix)
    {
        if (segments.Length < prefix.Length) return false;
        for (var i = 0; i < prefix.Length; i++)
        {
            if (!string.Equals(segments[i], prefix[i], StringComparison.Ordinal)) return false;
        }
        return true;
    }

    private List<CustomCommandsInstantProvider.CommandItem> LoadQuickNavCommands()
    {
        if (_cache != null) return _cache;
        try
        {
            var cmds = PluginSettingsService.GetSetting<List<CustomCommandsInstantProvider.CommandItem>>(
                "Lertaro.Plugins.CustomCommands", "Commands", null!);
            _cache = cmds?.Where(c => c.Enabled && c.ShowInQuickNav && !string.IsNullOrWhiteSpace(c.Path)).ToList()
                     ?? new List<CustomCommandsInstantProvider.CommandItem>();
        }
        catch
        {
            _cache = new List<CustomCommandsInstantProvider.CommandItem>();
        }
        return _cache;
    }
}
