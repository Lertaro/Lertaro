using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.FileFilters;

public class FileFiltersSearchableItemProvider : ISearchableItemProvider, IDisposable
{
    public string Id => "FileFiltersSearchableItemProvider";
    public string Name => TranslationService.Get("FileFilters_ProviderName");

    public event Action? ItemsChanged;

    public class FilterItem
    {
        public bool Enabled { get; set; } = true;
        public string Name { get; set; } = string.Empty;
        public string Keyword { get; set; } = string.Empty;
        public List<string> Folders { get; set; } = new();
        public string FilterPattern { get; set; } = "*";
    }

    private readonly List<FilterItem> _registeredFilters = new();

    private readonly object _runtimeLock = new();
    private IDisposable? _directoryWatch;
    private bool _disposed;

    public FileFiltersSearchableItemProvider()
    {
        PluginSettingsService.SettingChanged += OnSettingChanged;
        PluginSettingsService.ComponentEnablementChanged += OnComponentEnablementChanged;
        UpdateRuntimeState();
    }

    private bool IsComponentEnabled => PluginSettingsService.IsComponentEnabled(
        Path.GetFileName(typeof(FileFiltersSearchableItemProvider).Assembly.Location),
        "SearchableItemProvider", nameof(FileFiltersSearchableItemProvider));

    private void OnComponentEnablementChanged() => UpdateRuntimeState();

    private void UpdateRuntimeState()
    {
        lock (_runtimeLock)
        {
            if (_disposed)
                return;

            if (IsComponentEnabled)
            {
                if (_directoryWatch != null)
                    return;

                _directoryWatch = DirectoryIndexerService.WatchDirectories("FileFilters", () => ItemsChanged?.Invoke());
                ReloadFilters();
                return;
            }

            _directoryWatch?.Dispose();
            _directoryWatch = null;
            DirectoryIndexerService.UnregisterDirectories("FileFilters");
            _registeredFilters.Clear();
        }
    }

    private void OnSettingChanged(string pluginId, string key)
    {
        if (string.Equals(pluginId, "Lertaro.Plugins.FileFilters", StringComparison.OrdinalIgnoreCase)
            && string.Equals(key, "Filters", StringComparison.OrdinalIgnoreCase))
        {
            if (!IsComponentEnabled)
                return;

            ReloadFilters();
            ItemsChanged?.Invoke();
        }
    }

    private void ReloadFilters()
    {
        // Unregister old ones
        DirectoryIndexerService.UnregisterDirectories("FileFilters");

        _registeredFilters.Clear();
        var filters = PluginSettingsService.GetSetting<List<FilterItem>>("Lertaro.Plugins.FileFilters", "Filters", null!);
        if (filters != null)
        {
            foreach (var f in filters.Where(x => x.Enabled))
            {
                _registeredFilters.Add(f);

                if (f.Folders != null)
                {
                    foreach (var path in f.Folders.Select(p => p.Trim()).Where(p => !string.IsNullOrEmpty(p) && Directory.Exists(p)))
                    {
                        DirectoryIndexerService.RegisterDirectory("FileFilters", path, recursive: true, filterPattern: f.FilterPattern);
                    }
                }
            }
        }
    }

    public IEnumerable<SearchableItem> GetSearchableItems()
    {
        if (!IsComponentEnabled)
            return Array.Empty<SearchableItem>();

        var items = new List<SearchableItem>();


        foreach (var filter in _registeredFilters)
        {
            if (filter.Folders == null) continue;

            // Prefix Name if present to display in description (e.g. "Movies · Z:\av")
            var filterPrefix = !string.IsNullOrWhiteSpace(filter.Name)
                ? $"{filter.Name.Trim()} · "
                : string.Empty;

            // Shared by both the file and folder loops below -- same SearchableItem shape either way,
            // only the default (no-keyword) ResultKind and the log message differ. UseShellExecute on a
            // directory path opens it in Explorer just as well as it opens a file with its default app,
            // so OnExecute needs no branching between the two.
            SearchableItem BuildItem(string path, string defaultResultKind)
            {
                var name = Path.GetFileName(path);
                var parentDir = Path.GetDirectoryName(path) ?? string.Empty;
                var desc = filterPrefix + parentDir;

                // Assign unique ResultKind code pattern for keyword routing isolation (e.g. "FileFilter_tf")
                var resultKind = string.IsNullOrEmpty(filter.Keyword) ? defaultResultKind : $"FileFilter_{filter.Keyword.Trim().ToLowerInvariant()}";

                return new SearchableItem
                {
                    Title = name, // Clean title
                    Description = desc,
                    ResultKind = resultKind,
                    HBitmapIcon = IntPtr.Zero, // Retain null so ShellIconHelper loads high fidelity video thumbnails dynamically!
                    ActionType = "None",
                    ActionArgument = path,
                    OnExecute = () =>
                    {
                        try
                        {
                            var psi = new System.Diagnostics.ProcessStartInfo
                            {
                                FileName = path,
                                UseShellExecute = true
                            };
                            System.Diagnostics.Process.Start(psi);
                        }
                        catch (Exception ex)
                        {
                            PluginSdk.Logger.Log($"[FileFilters] Failed to open '{path}': {ex.Message}", PluginSdk.LogLevel.Error);
                        }
                    }
                };
            }

            foreach (var root in filter.Folders.Select(p => p.Trim()).Where(p => !string.IsNullOrEmpty(p)))
            {
                try
                {
                    // One walk for the whole subtree, answered from the host's file index rather than by
                    // reading the disk -- which is what makes a filter over a large or sleeping folder
                    // cost nothing to rebuild. The host applies FilterPattern (a file pattern; folders
                    // come back regardless, which is exactly what this provider wants), skips
                    // hidden/system entries, and falls back to a real filesystem walk on its own for a
                    // folder no index covers -- an unindexed drive, a network share, a path that doesn't
                    // exist. So there is deliberately no Directory.Exists check and no live-scan branch
                    // here anymore: both are the host's job now, and it can answer them without waking
                    // the disk in the common case.
                    //
                    // Blocking on the async sequence is fine and intentional: ISearchableItemProvider is
                    // synchronous, and SearchableItemCache already calls this from a Task.Run.
                    foreach (var entry in DirectoryIndexerService
                        .EnumerateDirectoryAsync(root, recursive: true, filterPattern: filter.FilterPattern)
                        .ToBlockingEnumerable())
                    {
                        if (!string.IsNullOrEmpty(entry.Name))
                            items.Add(BuildItem(entry.FullPath, entry.IsDir ? "Directory" : "File"));
                    }
                }
                catch (Exception ex)
                {
                    PluginSdk.Logger.Log($"[FileFilters] Error listing directory '{root}': {ex.Message}", PluginSdk.LogLevel.Warn);
                }
            }
        }

        return items;
    }

    public void Dispose()
    {
        lock (_runtimeLock)
        {
            if (_disposed)
                return;

            _disposed = true;
            _directoryWatch?.Dispose();
            _directoryWatch = null;
            PluginSettingsService.SettingChanged -= OnSettingChanged;
            PluginSettingsService.ComponentEnablementChanged -= OnComponentEnablementChanged;
            DirectoryIndexerService.UnregisterDirectories("FileFilters");
            _registeredFilters.Clear();
        }

        GC.SuppressFinalize(this);
    }
}
