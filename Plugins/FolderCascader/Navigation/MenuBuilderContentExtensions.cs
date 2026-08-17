using System.IO;
using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Abstractions.Plugins.WindowAdapters;
using Lertaro.PluginSdk.Registries;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.FolderCascader.Navigation;

// Per-menu-context content builders for MenuBuilder.GetMenuItems, split out (composition, not a partial
// class) to keep MenuBuilder.cs under the project's line limit. MenuBuilder itself keeps GetMenuItems (the
// dispatch entry point), the publicly-tested category-path helpers, and small shared utilities -- this
// file has no surface any test calls directly, only what GetMenuItems' own dispatch delegates into.
internal static class MenuBuilderContentExtensions
{
    internal static List<DynamicMenuItem> BuildRootMenu(Provider provider)
    {
        provider.ClearSession();
        var items = new List<DynamicMenuItem>();

        // Unpersisted falls back to FolderCascaderPlugin's own schema DefaultValue automatically
        // -- see PluginManager.GetSettingFunc -- so there's no separate hardcoded default here.
        var folders = PluginSettingsService.GetSetting(
            "Lertaro.Plugins.FolderCascader",
            "Folders",
            new List<FolderCascaderPlugin.FolderConfigItem>());

        if (folders != null)
        {
            MenuBuilder.AddFolderItems(items, folders, Array.Empty<string>(), provider);
        }

        if (GetUniqueOpenedFolderPaths(OpenedFolderCollectorRegistry.GetOpenedFolders()).Count > 0)
        {
            if (items.Count > 0 && !items.Last().IsSeparator)
            {
                items.Add(new DynamicMenuItem { IsSeparator = true });
            }
            items.Add(new DynamicMenuItem
            {
                Text = TranslationService.Get("FolderCascader_OpenedFolders"),
                HasSubMenu = true,
                SubMenuHandle = provider.AllocateHandle("foldercascader://opened-folders"),
                HBitmapItem = IconBitmapCache.OpenedFoldersHBitmap
            });
        }

        var showFavorites = PluginSettingsService.GetSetting(
            "Lertaro.Plugins.FolderCascader",
            "ShowFavorites",
            true);

        var showHistory = PluginSettingsService.GetSetting(
            "Lertaro.Plugins.FolderCascader",
            "ShowHistory",
            true);

        var favoritesList = FavoritesService.GetFavorites()
             .Where(p => !string.IsNullOrEmpty(p.Path))
             .ToList();

        if (showFavorites && favoritesList.Count > 0)
        {
            if (items.Count > 0 && !items.Last().IsSeparator)
            {
                items.Add(new DynamicMenuItem { IsSeparator = true });
            }
            items.Add(new DynamicMenuItem
            {
                Text = TranslationService.Get("FolderCascader_Favorites"),
                HasSubMenu = true,
                SubMenuHandle = provider.AllocateHandle("foldercascader://favorites"),
                HBitmapItem = IconBitmapCache.FavoritesHBitmap
            });
        }

        if (showHistory && HistoryService.GetHistoryEntries().Take(30).ToList().Count > 0)
        {
            if (items.Count > 0 && !items.Last().IsSeparator)
            {
                items.Add(new DynamicMenuItem { IsSeparator = true });
            }
            items.Add(new DynamicMenuItem
            {
                Text = TranslationService.Get("FolderCascader_History"),
                HasSubMenu = true,
                SubMenuHandle = provider.AllocateHandle("foldercascader://history"),
                HBitmapItem = IconBitmapCache.HistoryHBitmap
            });
        }

        while (items.Count > 0 && items.Last().IsSeparator)
        {
            items.RemoveAt(items.Count - 1);
        }

        return items;
    }

    internal static List<DynamicMenuItem> BuildHistoryMenu(Provider provider)
    {
        var items = new List<DynamicMenuItem>();
        var recentEntries = HistoryService.GetHistoryEntries().Take(30).ToList();
        foreach (var entry in recentEntries)
        {
            var rpath = entry.Path;
            if (string.IsNullOrWhiteSpace(rpath)) continue;

            // An app-type entry is always a launchable leaf, never a browsable folder -- and
            // its path (a real exe path, or a virtual shell:AppsFolder\{AUMID} id) can't be
            // existence-checked with Directory.Exists/File.Exists the way a real path can.
            if (entry.Kind == HistoryEntryKind.Application)
            {
                items.Add(new DynamicMenuItem
                {
                    Text = MenuBuilder.GetDisplayName(rpath, ""),
                    CommandId = provider.AllocateCommand(rpath),
                    HBitmapItem = IntPtr.Zero
                });
            }
            else if (!HistoryEntryExists(entry, File.Exists, Directory.Exists))
            {
                continue;
            }
            else if (entry.Kind == HistoryEntryKind.Folder)
            {
                items.Add(new DynamicMenuItem
                {
                    Text = MenuBuilder.GetDisplayName(rpath, ""),
                    HasSubMenu = true,
                    SubMenuHandle = provider.AllocateHandle(rpath),
                    HBitmapItem = IntPtr.Zero
                });
            }
            else
            {
                items.Add(new DynamicMenuItem
                {
                    Text = Path.GetFileName(rpath) + $" ({Path.GetDirectoryName(rpath)})",
                    CommandId = provider.AllocateCommand(rpath),
                    HBitmapItem = IntPtr.Zero
                });
            }
        }
        if (items.Count == 0)
            items.Add(new DynamicMenuItem { Text = TranslationService.Get("FolderCascader_NoHistory"), IsDisabled = true });
        return items;
    }

    internal static List<DynamicMenuItem> BuildOpenedFoldersMenu(IEnumerable<OpenedFolder> folders, Provider provider)
    {
        var items = new List<DynamicMenuItem>();
        foreach (var path in GetUniqueOpenedFolderPaths(folders))
        {
            items.Add(new DynamicMenuItem
            {
                Text = MenuBuilder.GetDisplayName(path, ""),
                HasSubMenu = true,
                SubMenuHandle = provider.AllocateHandle(path),
                HBitmapItem = IconBitmapCache.FolderHBitmap
            });
        }
        return items;
    }

    // This list may combine multiple tabs, panes, and adapters. Collapse only lexical duplicates:
    // probing the filesystem here would make merely opening the menu perform unexpected disk I/O.
    internal static List<string> GetUniqueOpenedFolderPaths(IEnumerable<OpenedFolder> folders)
    {
        var paths = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in folders)
        {
            if (string.IsNullOrWhiteSpace(folder.Path)) continue;

            var path = Path.TrimEndingDirectorySeparator(folder.Path);
            if (seen.Add(path)) paths.Add(path);
        }
        return paths;
    }

    internal static bool HistoryEntryExists(
        HistoryEntry entry,
        Func<string, bool> fileExists,
        Func<string, bool> directoryExists) => entry.Kind switch
        {
            HistoryEntryKind.Application => true,
            HistoryEntryKind.Folder => directoryExists(entry.Path),
            HistoryEntryKind.File => fileExists(entry.Path),
            _ => false
        };

    internal static List<DynamicMenuItem> BuildFavoritesMenu(Provider provider)
    {
        var items = new List<DynamicMenuItem>();
        var favoritesList = FavoritesService.GetFavorites()
            .Where(p => !string.IsNullOrEmpty(p.Path))
            .ToList();

        foreach (var favItem in favoritesList)
        {
            var favPath = favItem.Path;
            var isVirtual = favPath.StartsWith("::") || favPath.StartsWith("shell:");
            if (isVirtual || Directory.Exists(favPath))
            {
                items.Add(new DynamicMenuItem
                {
                    Text = MenuBuilder.GetDisplayName(favPath, favItem.Name),
                    HasSubMenu = true,
                    SubMenuHandle = provider.AllocateHandle(favPath),
                    HBitmapItem = IntPtr.Zero
                });
            }
            else if (File.Exists(favPath))
            {
                items.Add(new DynamicMenuItem
                {
                    Text = string.IsNullOrWhiteSpace(favItem.Name) ? Path.GetFileName(favPath) : favItem.Name,
                    CommandId = provider.AllocateCommand(favPath),
                    HBitmapItem = IntPtr.Zero
                });
            }
            else if (MenuBuilder.IsWebUrl(favPath))
            {
                // Web-address favorite: a leaf command item. The host renders the globe icon and
                // opens it in the browser (both keyed off the http/https path).
                items.Add(new DynamicMenuItem
                {
                    Text = string.IsNullOrWhiteSpace(favItem.Name) ? favPath : favItem.Name,
                    CommandId = provider.AllocateCommand(favPath),
                    HBitmapItem = IntPtr.Zero
                });
            }
        }
        if (items.Count == 0)
            items.Add(new DynamicMenuItem { Text = TranslationService.Get("FolderCascader_NoFavorites"), IsDisabled = true });
        return items;
    }

    internal static List<DynamicMenuItem> BuildCategoryMenu(ISearchResult result, string[] categoryPrefix, Provider provider)
    {
        var items = new List<DynamicMenuItem>();
        // A submenu category node (see AddFolderItems), not a real filesystem path -- reload
        // the same Folders setting the root level did and re-run the grouping logic scoped to
        // this category's prefix, same as CustomCommandsQuickNavProvider re-partitions its own
        // flat list on every submenu expansion instead of building a tree once up front.
        var folders = PluginSettingsService.GetSetting(
            "Lertaro.Plugins.FolderCascader",
            "Folders",
            new List<FolderCascaderPlugin.FolderConfigItem>());
        if (folders != null)
        {
            MenuBuilder.AddFolderItems(items, folders, categoryPrefix, provider);
        }
        while (items.Count > 0 && items.Last().IsSeparator)
        {
            items.RemoveAt(items.Count - 1);
        }
        if (items.Count == 0)
            items.Add(new DynamicMenuItem { Text = TranslationService.Get("FolderCascader_EmptyFolder"), IsDisabled = true });

        MenuBuilder.InsertCategoryHeader(items, result, categoryPrefix);
        return items;
    }

}
