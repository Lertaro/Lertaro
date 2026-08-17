using System.IO;
using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Services;
using Lertaro.PluginSdk.Helpers;

namespace Lertaro.Plugins.FolderCascader.Navigation;

public static class MenuBuilder
{
    public static IEnumerable<DynamicMenuItem> GetMenuItems(ISearchResult result, IntPtr hMenu, Provider provider)
    {
        IconBitmapCache.EnsureIcons();

        if (hMenu == IntPtr.Zero)
            return MenuBuilderContentExtensions.BuildRootMenu(provider);

        if (provider.TryGetFolderPage(hMenu, out var folderPage) && folderPage != null)
            return FolderBrowseMenuBuilder.Build(folderPage.Path, folderPage.Offset, provider);

        if (!provider.TryGetPath(hMenu, out var path) || path == null)
            return Enumerable.Empty<DynamicMenuItem>();

        if (path == "foldercascader://history")
            return MenuBuilderContentExtensions.BuildHistoryMenu(provider);

        if (path == "foldercascader://favorites")
            return MenuBuilderContentExtensions.BuildFavoritesMenu(provider);

        if (path == "foldercascader://opened-folders")
            return MenuBuilderContentExtensions.BuildOpenedFoldersMenu(ExplorerPathService.GetOpenedFolderPaths(), provider);

        if (TryDecodeCategoryPath(path, out var categoryPrefix))
            return MenuBuilderContentExtensions.BuildCategoryMenu(result, categoryPrefix, provider);

        return FolderBrowseMenuBuilder.Build(path, offset: 0, provider);
    }

    internal static string GetDisplayName(string path, string customName)
    {
        if (!string.IsNullOrWhiteSpace(customName)) return customName;
        // "shell:" covers both the "shell:::{CLSID}" virtual-folder form and "shell:AppsFolder\{AUMID}"
        // (packaged apps) -- not just the CLSID form -- matching the isVirtual check already used for
        // favorites above.
        if (path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase) || path.StartsWith("::", StringComparison.OrdinalIgnoreCase))
            return ShellPathHelper.GetVirtualFolderDisplayName(path, path);
        try
        {
            var name = Path.GetFileName(path.TrimEnd('\\', '/'));
            return string.IsNullOrEmpty(name) ? path : name;
        }
        catch { return path; }
    }

    internal static bool IsWebUrl(string path)
        => Uri.TryCreate(path?.Trim(), UriKind.Absolute, out var uri)
           && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    private const string CategoryPathPrefix = "foldercascader://category/";

    // Category submenus have no host-rendered header the way the root level does (see Provider.
    // HeaderAction, wired into QuickNavigationMenu.Show's own per-provider group header) -- this is
    // the plugin-owned equivalent for a nested level, prepended as the submenu's own first item so it
    // reads as this level's title, not an ordinary row. Text is just the category's own last path
    // segment ("Network" for "Tools/Network"), not the full prefix, matching how that same category
    // shows up as a single "Network" entry one level up.
    internal static void InsertCategoryHeader(List<DynamicMenuItem> items, ISearchResult result, string[] prefix)
    {
        var folderPath = result.FullPath;
        var subMenu = string.Join("/", prefix);
        items.Insert(0, new DynamicMenuItem
        {
            IsHeader = true,
            Text = prefix.Length > 0 ? prefix[^1] : string.Empty,
            OnExecute = () => PromptAndAddCurrentFolder(folderPath, subMenu)
        });
    }

    // Asks for Name/Path/SubMenu (pre-filled from what was clicked) before adding, reusing the exact
    // same fields -- key, label, description -- the real Configure dialog's Folders array uses for
    // these three, via PluginPromptService rather than a bespoke input dialog. All three stay editable
    // (not just Name): the user might want to add a sibling path, or file it under a different
    // category than the one they happened to click "Add" from. A null result means the prompt was
    // cancelled or the host hasn't wired PluginPromptService up (an older host build) -- either way,
    // nothing gets added rather than silently adding an entry the user never confirmed.
    internal static void PromptAndAddCurrentFolder(string folderPath, string subMenu)
    {
        var nameField = new PluginConfigField
        {
            Key = "Name",
            LabelKey = "FolderCascader_Config_FolderName",
            FieldType = ConfigFieldType.Text,
            DefaultValue = GetDisplayName(folderPath, "")
        };
        var pathField = new PluginConfigField
        {
            Key = "Path",
            LabelKey = "FolderCascader_Config_FolderPath",
            FieldType = ConfigFieldType.FolderPath,
            DefaultValue = folderPath
        };
        var subMenuField = new PluginConfigField
        {
            Key = "SubMenu",
            LabelKey = "FolderCascader_Config_SubMenuLabel",
            DescriptionKey = "FolderCascader_Config_SubMenuDesc",
            FieldType = ConfigFieldType.Text,
            DefaultValue = subMenu
        };

        var values = PluginPromptService.Prompt(
            TranslationService.Get("FolderCascader_AddCurrentFolder"),
            new[] { nameField, pathField, subMenuField });
        if (values == null) return;

        var name = values.TryGetValue("Name", out var n) ? n as string ?? "" : "";
        var path = values.TryGetValue("Path", out var p) ? p as string ?? "" : "";
        var editedSubMenu = values.TryGetValue("SubMenu", out var s) ? s as string ?? "" : "";
        CommandExecutor.AddCurrentFolder(string.IsNullOrWhiteSpace(path) ? folderPath : path, editedSubMenu, name);
    }

    // Groups configured folders by their SubMenu field and appends the items belonging at exactly
    // "prefix" depth -- a leaf (Name/Path) entry for folders whose SubMenu matches prefix exactly, or
    // (at most once per distinct next segment) a HasSubMenu category entry for folders nested deeper.
    // Same re-partition-a-flat-list-on-every-expansion technique CustomCommandsQuickNavProvider uses
    // for its own SubMenu field, rather than building a tree once up front.
    internal static void AddFolderItems(List<DynamicMenuItem> items, List<FolderCascaderPlugin.FolderConfigItem> folders, string[] prefix, Provider provider)
    {
        var seenCategories = new HashSet<string>(StringComparer.Ordinal);
        foreach (var folder in folders)
        {
            var segments = SplitSubMenuPath(folder.SubMenu);
            if (!StartsWithPrefix(segments, prefix)) continue;

            if (segments.Length > prefix.Length)
            {
                var category = segments[prefix.Length];
                if (!seenCategories.Add(category)) continue;

                var childPrefix = new string[prefix.Length + 1];
                Array.Copy(prefix, childPrefix, prefix.Length);
                childPrefix[prefix.Length] = category;

                items.Add(new DynamicMenuItem
                {
                    Text = category,
                    HasSubMenu = true,
                    SubMenuHandle = provider.AllocateHandle(EncodeCategoryPath(childPrefix)),
                    HBitmapItem = IconBitmapCache.CategoryHBitmap,
                    // QuickNavigationMenu's own root-level click-suppression (isRootItem && HasSubMenu)
                    // only ever applies at the very top level -- every nested submenu (any category one
                    // level deep or more, e.g. this one when prefix.Length > 0) is built through
                    // QuickNavigationSubMenuLoader, which never passes isRootItem: true. IsActionable is
                    // the only gate that reaches those, so it has to be set explicitly here regardless of
                    // depth, not just relied on implicitly like the root case.
                    IsActionable = false
                });
                continue;
            }

            // segments.Length == prefix.Length: this entry belongs exactly at the current level.
            if (folder.Path == "-" || folder.Name == "-")
            {
                items.Add(new DynamicMenuItem { IsSeparator = true });
                continue;
            }
            if (string.IsNullOrWhiteSpace(folder.Path)) continue;
            var pathExists = true;
            if (!folder.Path.StartsWith("::") && !folder.Path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
            {
                pathExists = Directory.Exists(folder.Path);
            }
            items.Add(new DynamicMenuItem
            {
                Text = GetDisplayName(folder.Path, folder.Name),
                HasSubMenu = pathExists,
                SubMenuHandle = pathExists ? provider.AllocateHandle(folder.Path) : IntPtr.Zero,
                HBitmapItem = IntPtr.Zero,
                IsDisabled = !pathExists
            });
        }
    }

    // Empty segments (e.g. "a//b", "a/", "/a") are dropped rather than producing an empty-named
    // category or erroring -- a stray typo in the config shouldn't break navigation.
    internal static string[] SplitSubMenuPath(string subMenu) =>
        string.IsNullOrWhiteSpace(subMenu)
            ? Array.Empty<string>()
            : subMenu.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    // Case-sensitive by design, matching CustomCommandsQuickNavProvider's own SubMenu grouping:
    // "Tools" and "tools" are two distinct categories, never merged.
    internal static bool StartsWithPrefix(string[] segments, string[] prefix)
    {
        if (segments.Length < prefix.Length) return false;
        for (var i = 0; i < prefix.Length; i++)
        {
            if (!string.Equals(segments[i], prefix[i], StringComparison.Ordinal)) return false;
        }
        return true;
    }

    internal static string EncodeCategoryPath(string[] segments) => CategoryPathPrefix + string.Join("/", segments);

    internal static bool TryDecodeCategoryPath(string path, out string[] segments)
    {
        if (!path.StartsWith(CategoryPathPrefix, StringComparison.Ordinal))
        {
            segments = Array.Empty<string>();
            return false;
        }
        segments = path.Substring(CategoryPathPrefix.Length).Split('/', StringSplitOptions.RemoveEmptyEntries);
        return true;
    }
}
