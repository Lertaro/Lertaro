using System.IO;
using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Helpers;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.FolderCascader.Navigation;

// Split out from MenuBuilderContentExtensions to keep the paging, snapshot, and menu rendering for a
// physical folder together without taking the shared content-builder file over the line limit.
internal static class FolderBrowseMenuBuilder
{
    internal static List<DynamicMenuItem> Build(string path, int offset, Provider provider)
    {
        var items = new List<DynamicMenuItem>();
        try
        {
            var scanPath = ResolvePhysicalPath(path);
            if (Directory.Exists(scanPath))
            {
                AddPhysicalFolderPage(items, scanPath, offset, provider);
            }
            else if (IsVirtualPath(scanPath))
            {
                ShellEnumerator.EnumerateShellFolder(scanPath, items, provider);
            }

            if (items.Count == 0)
                items.Add(new DynamicMenuItem { Text = TranslationService.Get("FolderCascader_EmptyFolder"), IsDisabled = true });
        }
        catch
        {
            items.Add(new DynamicMenuItem { Text = TranslationService.Get("FolderCascader_EmptyFolder"), IsDisabled = true });
        }
        return items;
    }

    private static void AddPhysicalFolderPage(List<DynamicMenuItem> items, string path, int offset, Provider provider)
    {
        var snapshot = provider.GetFolderSnapshot(path);
        var page = snapshot.GetPage(offset);
        foreach (var entry in page)
        {
            items.Add(entry.IsDirectory
                ? new DynamicMenuItem
                {
                    Text = Path.GetFileName(entry.Path),
                    HasSubMenu = true,
                    SubMenuHandle = provider.AllocateHandle(entry.Path)
                }
                : new DynamicMenuItem
                {
                    Text = Path.GetFileName(entry.Path),
                    CommandId = provider.AllocateCommand(entry.Path)
                });
        }

        var nextOffset = offset + page.Count;
        if (nextOffset < snapshot.Count)
        {
            items.Add(new DynamicMenuItem
            {
                HasSubMenu = true,
                IsActionable = false,
                IsContinuation = true,
                SubMenuHandle = provider.AllocateFolderPage(path, nextOffset)
            });
        }
    }

    private static string ResolvePhysicalPath(string path)
    {
        if (!IsVirtualPath(path)) return path;

        var resolved = ShellPathHelper.TryResolveVirtualPath(path);
        return Directory.Exists(resolved) ? resolved : path;
    }

    private static bool IsVirtualPath(string path) =>
        path.StartsWith("::", StringComparison.Ordinal) || path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase);
}
