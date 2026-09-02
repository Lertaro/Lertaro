using System.Diagnostics;
using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.CustomActions;

public class DynamicActionProvider : IDynamicActionProvider
{
    public string GroupName => TranslationService.Get("CustomActions_GroupName");

    private const string DefaultIcon =
        "M14 2H6c-1.1 0-1.99.9-1.99 2L4 20c0 1.1.89 2 1.99 2H18c1.1 0 2-.9 2-2V8l-6-6zm2 16H8v-2h8v2zm0-4H8v-2h8v2zm-3-5V3.5L18.5 9H13z";

    public class ActionItem
    {
        public bool Enabled { get; set; } = true;
        public string Title { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public string Parameter { get; set; } = string.Empty;
        public string Icon { get; set; } = string.Empty;
        public string WorkingDir { get; set; } = string.Empty;
        public bool RunSilently { get; set; } = false;
        public bool RunAsAdmin { get; set; } = false;
        public string Hotkey { get; set; } = string.Empty;
        public bool FolderOnly { get; set; } = false;
        public string Extensions { get; set; } = string.Empty;
        public bool MultiSelect { get; set; } = false;
    }

    // ponytail: permanent cache; UserSettings.Load() is already memory-cached but GetSetting
    // still deserializes JSON every call. Cache here so deserialization happens once per session.
    // Invalidated on ClearSession() (called when exiting actions mode / settings save).
    private static List<ActionItem>? _cache;

    static DynamicActionProvider() => PluginSettingsService.SettingChanged += (pluginId, key) =>
                                           {
                                               if (pluginId == "Lertaro.Plugins.CustomActions" && key == "Actions")
                                               {
                                                   _cache = null;
                                               }
                                           };

    private static List<ActionItem> LoadActions()
    {
        if (_cache != null) return _cache;
        try { _cache = PluginSettingsService.GetSetting<List<ActionItem>>("Lertaro.Plugins.CustomActions", "Actions", null!) ?? new(); }
        catch { _cache = new(); }
        return _cache;
    }

    public static void InvalidateCache() => _cache = null;

    public bool CanProvide(IReadOnlyList<ISearchResult> results) => true;

    public bool IsVisibleInMenu(IReadOnlyList<ISearchResult> results, SearchWindowType windowType)
        => LoadActions().Any(a => IsAvailableFor(a, results));

    public IEnumerable<(string Hotkey, Action Execute)> GetHotkeyActions(IReadOnlyList<ISearchResult> results)
    {
        var targets = results.ToArray();
        foreach (var cmd in LoadActions())
        {
            if (string.IsNullOrWhiteSpace(cmd.Hotkey) || !IsAvailableFor(cmd, targets)) continue;
            var c = cmd;
            var t = targets;
            yield return (c.Hotkey, () => RunMulti(c, t));
        }
    }

    public IEnumerable<DynamicMenuItem> GetMenuItems(IReadOnlyList<ISearchResult> results, IntPtr hMenu)
    {
        if (hMenu != IntPtr.Zero) yield break;

        var targets = results.ToArray();
        foreach (var cmd in LoadActions())
        {
            if (!IsAvailableFor(cmd, targets)) continue;

            var capturedCmd = cmd;
            var t = targets;

            yield return new DynamicMenuItem
            {
                Text = cmd.Title,
                CommandId = 0,
                ShortcutHint = cmd.Hotkey,
                OnExecute = () => RunMulti(capturedCmd, t)
            };
        }
    }

    // An action is available when it applies to every selected result AND either a single result
    // is selected or the action opts into multi-selection.
    private static bool IsAvailableFor(ActionItem cmd, IReadOnlyList<ISearchResult> results)
        => results.Count > 0 && (results.Count == 1 || cmd.MultiSelect) && results.All(r => IsApplicable(cmd, r));

    public void ExecuteCommand(IReadOnlyList<ISearchResult> results, uint commandId, IntPtr ownerHwnd) { }

    public void ClearSession() => _cache = null;

    private static bool IsApplicable(ActionItem cmd, ISearchResult result)
    {
        if (!cmd.Enabled) return false;
        if (string.IsNullOrWhiteSpace(cmd.Title) || string.IsNullOrWhiteSpace(cmd.Path)) return false;
        if (cmd.FolderOnly && !result.IsDir) return false;

        if (!result.IsDir && !string.IsNullOrWhiteSpace(cmd.Extensions))
        {
            var ext = Path.GetExtension(result.FullPath ?? "").ToLowerInvariant();
            var allowed = cmd.Extensions
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim().ToLowerInvariant());
            if (!allowed.Contains(ext)) return false;
        }

        return true;
    }

    private static void RunMulti(ActionItem cmd, IReadOnlyList<ISearchResult> results)
    {
        if (results.Count == 0) return;

        // %s and {} expand to every selected path, each quoted so it stays one argument, joined
        // by spaces — a single invocation receives all files (e.g. tool "a" "b" "c"). For a single
        // selection this is just the one path. Users must NOT wrap the placeholder in quotes.
        var allPaths = string.Join(" ", results.Select(r => ArgQuoting.Quote(r.FullPath)));
        var param = string.IsNullOrWhiteSpace(cmd.Parameter) ? allPaths
            : cmd.Parameter.Replace("%s", allPaths).Replace("{}", allPaths);

        var first = results[0];
        var workDir = cmd.WorkingDir;
        if (string.IsNullOrWhiteSpace(workDir))
            workDir = first.IsDir ? first.FullPath : Path.GetDirectoryName(first.FullPath) ?? "";

        var psi = new ProcessStartInfo
        {
            FileName = cmd.Path,
            Arguments = param,
            UseShellExecute = true
        };
        if (!string.IsNullOrWhiteSpace(workDir) && Directory.Exists(workDir))
            psi.WorkingDirectory = workDir;
        if (cmd.RunSilently) psi.WindowStyle = ProcessWindowStyle.Hidden;
        if (cmd.RunAsAdmin) psi.Verb = "runas";

        try { Process.Start(psi); }
        catch (Exception ex)
        {
            // A vanished/moved/renamed action target fails silently otherwise -- the user clicks
            // and nothing happens, with no trace. CreateNoWindow is not set here: it has no effect
            // under UseShellExecute (WindowStyle.Hidden is the effective suppression).
            PluginSdk.Logger.Log($"[CustomActions] Failed to launch '{cmd.Path}': {ex.Message}", PluginSdk.LogLevel.Error);
        }
    }
}
