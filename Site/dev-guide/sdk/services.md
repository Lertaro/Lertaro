# Host Services

The `Lertaro.PluginSdk.Services` namespace provides high-performance static services exposing the host application's algorithms, caches, and platform hooks to plugins.

## 1. Core Static Services Overview

| Host Service | Core Signatures | Key Capabilities |
| :--- | :--- | :--- |
| **`FuzzyMatchService`** | `bool IsMatch(string pattern, string text)`<br>`bool[]? GetHighlightMask(string text, string query)`<br>`double GetMatchScore(string text, string query)` | Executes the host's fzf fuzzy matching engine, calculates character-level highlight masks with multi-tier fallback, and exposes the host's match-quality score for consistent result ranking. |
| **`TranslationService`** | `string Get(string key)`<br>`string Format(string key, params object[] args)`<br>`void LoadEmbeddedTranslations(...)`<br>`string GetCurrentCulture()`<br>`event Action<string>? CultureChanged` | Dynamic localization and runtime culture change broadcasts. `GetCurrentCulture()` returns the UI language code configured in Settings (e.g. `"zh-CN"`), independent of OS defaults; subscribe to `CultureChanged` to reload dictionaries or refresh internal state when the user switches UI language. |
| **`IconService`** | `ImageSource? GetIcon(string path, bool isDir)`<br>`ImageSource? GetThumbnail(string path, int size)` | Windows Shell icon and thumbnail extraction with integrated memory and disk caching. |
| **`FavoritesService`** | `IReadOnlyList<FavoriteItem> GetFavorites()`<br>`bool IsFavorite(string path)`<br>`bool TryAddFavorite(FavoriteItem favorite)` | Reads favorites, checks whether a path is already registered, and adds a favorite through the host bridge. |
| **`HistoryService`** | `IReadOnlyList<HistoryEntry> GetHistoryEntries()` | Reads historical launches sorted by recent access, including query keywords and entry types. |
| **`FileMetadataService`** | `Task<IReadOnlyDictionary<string, FileMetadata>> GetMetadataAsync(IEnumerable<string> paths)` | Batch queries physical file sizes and timestamps for external paths not in the active result set. |
| **`DirectoryIndexerService`** | `void RegisterDirectory(string pluginId, string path, bool recursive, string? filterPattern)`<br>`IDisposable WatchDirectories(string pluginId, Action onChanged)`<br>`IDisposable WatchDirectories(string pluginId, Action<IReadOnlyList<string>> onChanged)`<br>`IAsyncEnumerable<ISearchResult> EnumerateDirectoryAsync(...)` | Registers custom folders for host-side indexed search and change tracking. Enumeration reads only the host's file index and is streamed; an uncovered directory yields an empty sequence, so callers must ensure it is covered by a configured local-drive, network, or folder index. The host does not perform a live filesystem fallback. Watch notifications are debounced and can include the affected directories; an empty list means that no narrower scope was available. |
| **`MemoryMaintenanceService`** | `void RequestTrim()` | Requests a deferred, host-controlled working-set trim after a plugin finishes a burst of temporary memory work. The request may be coalesced or ignored, and does not release live caches. |
| **`RecentFilesService`** | `Task<IReadOnlyList<ISearchResult>> GetRecentFilesAsync(IEnumerable<string> directories, int limit, int maxAgeMinutes, CancellationToken token)` | Queries the memory index in sub-milliseconds to aggregate recent files across configured folders. |
| **`ExplorerPathService`** | `string? GetLastActivePath()` | Retrieves the last directory browsed across File Explorer and all native file dialogs. |
| **`PluginSettingsService`** | `T GetSetting<T>(string pluginId, string key, T defaultValue)`<br>`bool IsComponentEnabled(string dllName, string componentType, string componentName)`<br>`event Action<string, string>? SettingChanged`<br>`event Action? ComponentEnablementChanged` | Reads persistent plugin settings and the host's per-component enablement state. |
| **`SettingsSearchService`** | `IReadOnlyList<SettingsSearchEntryInfo> GetEntries()`<br>`void Invalidate()` | Reads the host's current searchable settings entries and lets the host refresh its cached snapshot when dynamically contributed entries change. |
| **`SettingsWindowService`** | `bool ShowWindow(string? targetSection = null)`<br>`bool ShowEntry(SettingsSearchEntryInfo? entry)` | Requests that the host show its themed Settings window or navigate directly to a searchable entry, without launching a URI or another process. |
| **`SearchRefreshService`** | `void RefreshIfMatches(Func<string, bool> queryMatches)` | Notifies the host to re-evaluate matching active searches after asynchronous background operations complete. |
| **`UserDataService`** | `string GetUserDataDirectory()`<br>`string GetSharedDataDirectory()` | Returns the user-specific data folder and machine-wide shared data directory (e.g. for Python/Node runtimes). |
| **`Logger`** | `void Log(string message, LogLevel level = LogLevel.Info)` | Writes logs to `app.log`, visible in real-time within the Settings log viewer. |
| **`PluginPromptService`** | `Task<Dictionary<string, object?>?> Prompt(string title, IEnumerable<PluginConfigField> fields, ...)` | Displays a lightweight modal input dialog rendered directly from field schemas. |
| **`PluginMessageBoxService`** | `MessageBoxResult Show(string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon, MessageBoxResult defaultResult)` | Requests a host-owned message box so plugins can use the host's themed UI, with a platform fallback when no host handler is registered. |
| **`ExplorerService`** | `void OpenDirectory(string directoryPath, string? fileNameOrFilePath = null)` | Opens the specified directory or locates a file, respecting the host's configured third-party file manager (or Explorer tabs), with fallback to system Explorer. |

`SettingsSearchService.GetEntries()` returns entries whose indexes are valid only during the current host process. Pass an entry directly to `SettingsWindowService.ShowEntry(...)`; the SDK invokes the host callback and does not construct or launch `lertaro://` URIs.

### Component enablement and expensive runtime state

`PluginSettingsService.IsComponentEnabled(...)` reads the host's per-component switch. Components that own directory watchers, background workers, external runtimes, or other expensive state should check it before initializing that state and subscribe to `ComponentEnablementChanged` to start or stop it when the user changes the switch. The method returns `true` when no host callback is registered or the callback fails, so plugins remain usable outside the full host.

## 2. Shell Native File Operations

`Lertaro.PluginSdk.Shell.FileOperations` wraps the native Windows Shell `IFileOperation` COM interface, providing native progress dialogs, conflict prompts, and `Ctrl+Z` undo support:

```csharp
namespace Lertaro.PluginSdk.Shell.FileOperations;

// Batch paste or move as a single atomic Shell operation
public static class ShellPasteHelper
{
    public static void PasteAsync(
        IEnumerable<string> sourcePaths,
        string destinationFolder,
        bool move = false,
        Action? onCompleted = null);
}

// Recycle bin or permanent deletion
public static class ShellDeleteHelper
{
    public static void DeleteAsync(IEnumerable<string> paths, bool permanent = false);
}

// Virtual file extraction from drag-and-drop streams
public static class VirtualFileExtractor
{
    public static bool HasVirtualFiles(IDataObject dataObject);
    public static Task<IReadOnlyList<string>> Extract(IDataObject dataObject, string targetFolder);
    public static string ResolveDestination(string folder, string name); // Auto-renames to (2) on conflicts
}
```

> [!TIP]
> Shell helpers execute automatically on the SDK's dedicated STA worker thread (`ShellOperationStaWorker`), eliminating the need for plugins to manage COM apartment threading manually.

## 3. Application lifecycle and themed plugin windows

`AppLifecycleService.RequestRestart()` asks the host application to perform an orderly restart. The host starts the replacement process, waits for the current instance to finish its normal shutdown, and then exits; plugins do not need to launch the executable or shut down the host themselves. The method returns `true` when the host accepts the request.

For plugin-owned WPF content, `Lertaro.PluginSdk.Windows.PluginWindow` supplies the host's rounded, themed window frame. Set `ContentHostControl.Content` to the plugin view and add footer buttons through `Footer`. Use `PluginWindowMode.Window` for a normal taskbar window or `PluginWindowMode.Dialog` for a topmost dialog that is hidden from Alt+Tab. An omitted icon uses the host's default application icon.

```csharp
var window = new PluginWindow("My tool", 720, 470, PluginWindowMode.Dialog);
window.ContentHostControl.Content = new MyView();
window.Footer.Children.Add(new Button { Content = "OK", IsDefault = true });
window.ShowDialog();
```
