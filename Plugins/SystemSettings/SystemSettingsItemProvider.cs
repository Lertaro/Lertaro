using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Services;
using Lertaro.PluginSdk.Helpers;

namespace Lertaro.Plugins.SystemSettings;

/// <summary>
/// Searchable item provider that returns Windows system settings / Control Panel items
/// from the GodMode virtual folder (shell:::{ED7BA470-8E54-465E-825C-99712043E01C}).
/// </summary>
public class SystemSettingsItemProvider : ISearchableItemProvider
{
    public string Name => TranslationService.Get("SystemSettings_Name");

    public event Action? ItemsChanged
    {
        add { }
        remove { }
    }

    // GodMode — "All Tasks" virtual folder that lists every Control Panel item and task.
    private const string GodModePath = "shell:::{ED7BA470-8E54-465E-825C-99712043E01C}";

    public IEnumerable<SearchableItem> GetSearchableItems()
    {
        var list = new List<SearchableItem>();
        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType == null) return list;
            var shell = Activator.CreateInstance(shellType);
            if (shell == null) return list;

            dynamic dShell = shell;
            dynamic folder = dShell.NameSpace(GodModePath);
            if (folder == null) return list;

            var desc = TranslationService.Get("SystemSettings_Description");

            foreach (var item in folder.Items())
            {
                string name = item.Name;
                string path = item.Path;
                if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(path) || item.IsFolder)
                    continue;

                var hBitmap = ShellPathHelper.TryGetIconHBitmapForShellItem(item);
                var capturedPath = path;
                list.Add(new SearchableItem
                {
                    Title = name,
                    Description = desc,
                    HBitmapIcon = hBitmap,
                    ActionType = "None",
                    OnExecute = () => ShellInvokeHelper.InvokeShellItem(GodModePath, capturedPath)
                });
            }
        }
        catch { }

        return list;
    }
}
