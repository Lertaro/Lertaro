using System.IO;
using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Helpers;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.CoreExtensions.Providers.Indexing;

/// <summary>
/// Searchable item provider that scans all start menu and desktop folders
/// and indexes applications/shortcuts as first-class searchable items.
/// </summary>
public class StartMenuAppItemProvider : ISearchableItemProvider, IDisposable
{
    public string Name => TranslationService.Get("Plugins_StartMenuAppItemProviderName");

    /// <summary>What this provider's directories are registered and notified under.</summary>
    private const string RegistrationId = "CoreExtensions.StartMenu";

    public event Action? ItemsChanged;

    private readonly IDisposable _directoryWatch;

    public StartMenuAppItemProvider()
    {
        // Only this provider's own directories reach here, so there is nothing to check: the host knows
        // whose registration a change fell under and calls the one that owns it.
        _directoryWatch = DirectoryIndexerService.WatchDirectories(RegistrationId, () => ItemsChanged?.Invoke());
        PluginSettingsService.SettingChanged += OnSettingChanged;
        try
        {
            foreach (var root in StartMenuShortcutResolver.GetStartMenuRoots())
            {
                if (!Directory.Exists(root))
                    continue;

                // Register directory to the host system indexer for global monitoring and search
                DirectoryIndexerService.RegisterDirectory(RegistrationId, root, recursive: true, filterPattern: "*.lnk");
            }
        }
        catch (Exception ex)
        {
            PluginSdk.Logger.Log($"[StartMenuAppItemProvider] Failed to register directories to indexer: {ex.Message}", PluginSdk.LogLevel.Warn);
        }
    }

    private void OnSettingChanged(string pluginId, string key)
    {
        if (string.Equals(pluginId, "Lertaro.Plugins.CoreExtensions", StringComparison.OrdinalIgnoreCase)
            && string.Equals(key, "CustomFolders", StringComparison.OrdinalIgnoreCase))
        {
            ItemsChanged?.Invoke();
        }
    }

    public void Dispose()
    {
        _directoryWatch.Dispose();
        PluginSettingsService.SettingChanged -= OnSettingChanged;
        try
        {
            DirectoryIndexerService.UnregisterDirectories(RegistrationId);
        }
        catch { }
        GC.SuppressFinalize(this);
    }

    public IEnumerable<SearchableItem> GetSearchableItems()
    {
        var list = new List<SearchableItem>();
        var indexedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entriesByName = new Dictionary<string, List<(string Name, string Path)>>(StringComparer.OrdinalIgnoreCase);

        // 1. Collect scan roots: built-in Start Menu/Desktop + user-configured custom folders
        var roots = StartMenuShortcutResolver.GetStartMenuRoots().ToList();
        try
        {
            var customFolders = PluginSettingsService.GetSetting<List<string>>("Lertaro.Plugins.CoreExtensions", "CustomFolders", null!);
            if (customFolders != null)
            {
                foreach (var p in customFolders)
                {
                    if (string.IsNullOrWhiteSpace(p)) continue;
                    var expanded = Environment.ExpandEnvironmentVariables(p.Trim());
                    if (Directory.Exists(expanded)) roots.Add(expanded);
                }
            }
        }
        catch (Exception ex)
        {
            PluginSdk.Logger.Log($"[StartMenuAppItemProvider] Failed to load custom folders config: {ex.Message}", PluginSdk.LogLevel.Warn);
        }

        // 2. Gather all unique shortcut files from all roots
        foreach (var root in roots)
        {
            foreach (var path in EnumerateAppFiles(root))
            {
                if (!StartMenuShortcutResolver.ShouldIndex(path) || !indexedPaths.Add(path))
                    continue;

                var name = Path.GetFileNameWithoutExtension(path);
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                if (!entriesByName.TryGetValue(name, out var entries))
                {
                    entries = new List<(string Name, string Path)>();
                    entriesByName[name] = entries;
                }
                entries.Add((name, path));
            }
        }

        // 3. Deduplicate entries that have the same name by target executable path
        var deduped = new List<(string Name, string Path)>();
        foreach (var group in entriesByName.Values)
        {
            if (group.Count == 1)
            {
                deduped.Add(group[0]);
                continue;
            }

            var seenTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in group)
            {
                var target = StartMenuShortcutResolver.ResolveShortcutTarget(entry.Path) ?? entry.Path;
                if (seenTargets.Add(target))
                {
                    deduped.Add(entry);
                }
            }
        }

        // 4. Map to SearchableItem list with dynamic icon loading
        var descTemplate = TranslationService.Get("Search_ResultAppDir");
        foreach (var entry in deduped)
        {
            var capturedPath = entry.Path;
            var targetPath = StartMenuShortcutResolver.ResolveShortcutTarget(capturedPath) ?? capturedPath;
            var parentDir = Path.GetDirectoryName(targetPath);
            var desc = string.IsNullOrWhiteSpace(parentDir)
                ? TranslationService.Get("Search_ResultApp")
                : string.Format(descTemplate, parentDir);

            list.Add(new SearchableItem
            {
                Title = entry.Name,
                Description = desc,
                ResultKind = "Application",
                HBitmapIcon = IntPtr.Zero,
                ActionType = "None",
                ActionArgument = capturedPath,
                OnExecute = () =>
                {
                    try
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = capturedPath,
                            UseShellExecute = true
                        });
                    }
                    catch (Exception ex)
                    {
                        PluginSdk.Logger.Log($"[StartMenuAppItemProvider] Failed to launch '{entry.Name}': {ex.Message}", PluginSdk.LogLevel.Error);
                    }
                }
            });
        }

        // 5. Add modern packaged (UWP/MSIX) apps from shell:AppsFolder — Calculator, Notepad, Terminal,
        //    etc. have no .lnk on disk so the scan above misses them. Classic apps are mirrored into
        //    AppsFolder too, so dedupe by display name against what we already indexed (entriesByName
        //    holds every scanned shortcut name); only genuinely-new names (the packaged apps) survive.
        AppendAppsFolderApps(list, entriesByName.Keys);

        return list;
    }

    /// <summary>Every app file under <paramref name="root"/>, from the host's index where it has one.</summary>
    /// <remarks>
    /// Through the host rather than Directory.GetFiles: for a drive it indexes -- which the Start Menu
    /// and Desktop live on -- this costs no disk I/O at all, and the walk it replaces was a recursive
    /// one over trees that can hold hundreds of entries. A directory no index covers (a share, a drive
    /// with indexing off, or simply an index still building at startup) is walked live by the host on
    /// its own, so this never has to know which case it is in.
    ///
    /// Blocking, because ISearchableItemProvider.GetSearchableItems is synchronous by contract and this
    /// already runs on the background task SearchableItemCache loads providers on -- there is no UI
    /// thread here to free up.
    ///
    /// The host drops hidden and system entries, which the old walk did not. A shortcut deliberately
    /// hidden by its installer therefore stops appearing; that is the same rule every other search
    /// result in the app already follows, and a hidden shortcut is one the shell itself does not offer.
    /// </remarks>
    private static IEnumerable<string> EnumerateAppFiles(string root)
    {
        var files = new List<string>();
        try
        {
            var enumerate = DirectoryIndexerService.EnumerateDirectoryAsync(
                root, recursive: true, filterPattern: StartMenuShortcutResolver.AppFilePattern);

            var collect = Task.Run(async () =>
            {
                await foreach (var entry in enumerate.ConfigureAwait(false))
                {
                    // The pattern selects files, but directories come back regardless -- see
                    // EnumerateDirectoryAsync's own contract.
                    if (!entry.IsDir && !string.IsNullOrEmpty(entry.FullPath))
                        files.Add(entry.FullPath);
                }
            });
            collect.GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            // One unreadable root costs its own entries and nothing else, exactly as the walk it
            // replaced logged and carried on per directory.
            PluginSdk.Logger.Log($"[StartMenuAppItemProvider] Failed to enumerate '{root}': {ex.Message}", PluginSdk.LogLevel.Warn);
        }

        return files;
    }

    private static void AppendAppsFolderApps(List<SearchableItem> list, IEnumerable<string> alreadyIndexedNames)
    {
        var existingNames = new HashSet<string>(alreadyIndexedNames, StringComparer.OrdinalIgnoreCase);
        var appDesc = TranslationService.Get("Search_ResultApp");

        List<AppsFolderEnumerator.AppEntry> apps;
        try
        {
            apps = AppsFolderEnumerator.Enumerate();
        }
        catch (Exception ex)
        {
            PluginSdk.Logger.Log($"[StartMenuAppItemProvider] Failed to enumerate shell:AppsFolder: {ex.Message}", PluginSdk.LogLevel.Warn);
            return;
        }

        foreach (var app in apps)
        {
            if (string.IsNullOrWhiteSpace(app.Name) || !existingNames.Add(app.Name))
                continue; // already covered by a Start Menu shortcut (classic app), or a duplicate name

            var aumid = app.Aumid;
            // Packaged apps launch by AUMID via shell:AppsFolder; a classic entry may expose a real
            // file path instead (rare here, since those are usually deduped away) — launch it directly.
            var looksLikePath = aumid.Length > 2 && aumid[1] == ':';
            var launchTarget = looksLikePath ? aumid : $"shell:AppsFolder\\{aumid}";

            list.Add(new SearchableItem
            {
                Title = app.Name,
                Description = appDesc,
                ResultKind = "Application",
                HBitmapIcon = IntPtr.Zero,
                ActionType = "None",
                ActionArgument = launchTarget,
                OnExecute = () =>
                {
                    try
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = launchTarget,
                            UseShellExecute = true
                        });
                    }
                    catch (Exception ex)
                    {
                        PluginSdk.Logger.Log($"[StartMenuAppItemProvider] Failed to launch app '{app.Name}' ({aumid}): {ex.Message}", PluginSdk.LogLevel.Error);
                    }
                }
            });
        }
    }
}
