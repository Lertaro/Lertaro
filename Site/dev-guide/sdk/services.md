# Host Services

The `Lertaro.PluginSdk.Services` namespace provides high-performance static services exposing the host application's algorithms, caches, and platform hooks to plugins.

## 1. Core Static Services Overview

| Host Service | Core Signatures | Key Capabilities |
| :--- | :--- | :--- |
| **`FuzzyMatchService`** | `bool IsMatch(string pattern, string text)`<br>`bool[]? GetHighlightMask(string text, string query)` | Executes the host's fzf fuzzy matching engine and calculates character-level highlight masks with multi-tier fallback. |
| **`TranslationService`** | `string Get(string key)`<br>`string Format(string key, params object[] args)`<br>`void LoadEmbeddedTranslations(...)`<br>`string GetCurrentCulture()` | Dynamic localization. `GetCurrentCulture()` returns the language code configured in Settings (e.g. `"zh-CN"`), independent of OS defaults. |
| **`IconService`** | `ImageSource? GetIcon(string path, bool isDir)`<br>`ImageSource? GetThumbnail(string path, int size)` | Windows Shell icon and thumbnail extraction with integrated memory and disk caching. |
| **`FavoritesService`** | `IReadOnlyList<FavoriteItem> GetFavorites()` | Read-only access to user-saved starred favorite entries. |
| **`HistoryService`** | `IReadOnlyList<HistoryEntry> GetHistoryEntries()` | Reads historical launches sorted by recent access, including query keywords and entry types. |
| **`FileMetadataService`** | `Task<IReadOnlyDictionary<string, FileMetadata>> GetMetadataAsync(IEnumerable<string> paths)` | Batch queries physical file sizes and timestamps for external paths not in the active result set. |
| **`DirectoryIndexerService`** | `void RegisterDirectory(string pluginId, string path, bool recursive, string? filterPattern)`<br>`IDisposable WatchDirectories(string pluginId, Action onChanged)`<br>`IAsyncEnumerable<ISearchResult> EnumerateDirectoryAsync(...)` | Registers custom folders for background indexing and change tracking; streams directory contents without disk I/O. |
| **`RecentFilesService`** | `Task<IReadOnlyList<ISearchResult>> GetRecentFilesAsync(IEnumerable<string> directories, int limit, int maxAgeMinutes, CancellationToken token)` | Queries the memory index in sub-milliseconds to aggregate recent files across configured folders. |
| **`ExplorerPathService`** | `string? GetLastActivePath()` | Retrieves the last directory browsed across File Explorer and all native file dialogs. |
| **`PluginSettingsService`** | `T GetSetting<T>(string pluginId, string key, T defaultValue)`<br>`event Action<string, string>? SettingChanged` | Reads persistent plugin settings (user value > schema default > fallback). |
| **`SearchRefreshService`** | `void RefreshIfMatches(Func<string, bool> queryMatches)` | Notifies the host to re-evaluate matching active searches after asynchronous background operations complete. |
| **`UserDataService`** | `string GetUserDataDirectory()`<br>`string GetSharedDataDirectory()` | Returns the user-specific data folder and machine-wide shared data directory (e.g. for Python/Node runtimes). |
| **`Logger`** | `void Log(string message, LogLevel level = LogLevel.Info)` | Writes logs to `app.log`, visible in real-time within the Settings log viewer. |
| **`PluginPromptService`** | `Task<Dictionary<string, object?>?> Prompt(string title, IEnumerable<PluginConfigField> fields, ...)` | Displays a lightweight modal input dialog rendered directly from field schemas. |

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
