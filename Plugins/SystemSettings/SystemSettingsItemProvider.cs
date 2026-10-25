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

    // Declares the result kind this provider contributes. The host uses it for two things: filing rows under
    // the settings filter, and looking up this provider type in the user ordered result-type list -- that
    // lookup matches on this string, so leaving it unset meant the saved position for this type was never
    // read, and these rows stayed pinned at the front of the list as default instant results.
    public string? ItemResultKind => "Settings";

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
                // Windows exposes the localized task-link synonyms through the Shell property store.
                // A missing property must not discard an otherwise usable settings item.
                string[] keywords;
                try { keywords = ParseKeywords((object?)item.ExtendedProperty("System.Keywords")); }
                catch { keywords = []; }
                list.Add(new SearchableItem
                {
                    Title = name,
                    Keywords = keywords,
                    Description = desc,
                    HBitmapIcon = hBitmap,
                    ActionType = "None",
                    // Names the kind of row this is, so the host can file it under the settings filter and
                    // order it by the user preferred type instead of the default instant-result bucket.
                    ResultKind = "Settings",
                    OnExecute = () => ShellInvokeHelper.InvokeShellItem(GodModePath, capturedPath)
                });
            }
        }
        catch { }

        return list;
    }

    internal static string[] ParseKeywords(object? value)
    {
        // FolderItem2 may return a semicolon-delimited string or a SAFEARRAY of strings.
        var values = value switch
        {
            string text => new[] { text },
            Array array => array.OfType<string>(),
            _ => Enumerable.Empty<string>()
        };
        return values.SelectMany(text => text.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
