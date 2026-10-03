# Host Services

The `Lertaro.PluginSdk.Services` namespace provides high-performance static services exposing the host application's algorithms, caches, and platform hooks to plugins.

## 1. Core Static Services Overview

| Host Service | Core Signatures | Key Capabilities |
| :--- | :--- | :--- |
| **`FuzzyMatchService`** | `bool IsMatch(string pattern, string text)`<br>`bool[]? GetHighlightMask(string text, string query)`<br>`double GetMatchScore(string text, string query)` | Executes the host's fzf fuzzy matching engine, calculates character-level highlight masks with multi-tier fallback, and exposes the host's match-quality score for consistent result ranking. |
| **`TranslationService`** | `string Get(string key)`<br>`bool TryGet(string key, out string result)`<br>`string Format(string key, params object[] args)`<br>`string GetCurrentCulture()`<br>`IReadOnlyList<string> GetSupportedCultures(Assembly assembly)`<br>`Dictionary<string, string> LoadEmbeddedTranslations(Assembly assembly, string cultureKey, string typeName)`<br>`event Action<string>? CultureChanged` | Dynamic localization and runtime culture change broadcasts. `GetCurrentCulture()` returns the UI language code configured in Settings (e.g. `"zh-CN"`), independent of OS defaults; subscribe to `CultureChanged` to reload dictionaries or refresh internal state when the user switches UI language. `TryGet` answers whether a key resolved, and `Get` falls back to a visible `[key]` placeholder rather than throwing. `GetSupportedCultures(assembly)` lists the cultures an assembly's embedded resources cover; `LoadEmbeddedTranslations(assembly, cultureKey, typeName)` returns one culture's dictionary. |
| **`IconService`** | `ImageSource? GetIcon(string path, bool isDir)`<br>`ImageSource? GetThumbnail(string path, int size)`<br>`ImageSource? GetIconFromCacheOnly(string path, bool isDir, out bool needsLoad)` | Windows Shell icon and thumbnail extraction with integrated memory and disk caching. `GetIconFromCacheOnly` never touches the Shell: it returns what is already cached and reports through `needsLoad` whether a real load is still required, which is how a list can paint first and fill icons in afterwards. |
| **`FavoritesService`** | `IEnumerable<FavoriteItem> GetFavorites()`<br>`bool IsFavorite(string path)`<br>`bool TryAddFavorite(FavoriteItem favorite)` | Reads favorites, checks whether a path is already registered, and adds a favorite through the host bridge. |
| **`HistoryService`** | `IEnumerable<HistoryEntry> GetHistoryEntries()` | Reads historical launches sorted by recent access, including query keywords, entry types, and per-entry usage counts. Each physical path appears at most once under the keyword that most recently opened it. |
| **`FileMetadataService`** | `Task<IReadOnlyDictionary<string, FileMetadata>> GetMetadataAsync(IReadOnlyList<string> paths)` | Batch queries physical file sizes and timestamps for external paths not in the active result set. |
| **`DirectoryIndexerService`** | `void RegisterDirectory(string pluginId, string directoryPath, bool recursive = true, string filterPattern = "*")`<br>`void UnregisterDirectories(string pluginId)`<br>`IDisposable WatchDirectories(string pluginId, Action onChanged)`<br>`IDisposable WatchDirectories(string pluginId, Action<IReadOnlyList<string>> onChanged)`<br>`void NotifyDirectoryChanged(string pluginId)`<br>`void NotifyDirectoryChanged(string pluginId, IReadOnlyList<string> changedDirectories)`<br>`Task<List<ISearchResult>> SearchDirectoriesAsync(string pluginId, string query, CancellationToken token = default)`<br>`IAsyncEnumerable<ISearchResult> EnumerateDirectoryAsync(string directoryPath, bool recursive = false, string filterPattern = "*", int limit = 0, CancellationToken token = default)` | Registers custom folders for host-side indexed search and change tracking. Enumeration reads only the host's file index and is streamed; an uncovered directory yields an empty sequence, so callers must ensure it is covered by a configured local-drive, network, or folder index. The host does not perform a live filesystem fallback. Note the deliberate asymmetry: `RegisterDirectory` recurses by default while `EnumerateDirectoryAsync` does not. Watch notifications are debounced and can include the affected directories; an empty list means that no narrower scope was available. |
| **`MemoryMaintenanceService`** | `void RequestTrim()` | Requests a deferred, host-controlled working-set trim after a plugin finishes a burst of temporary memory work. The request may be coalesced or ignored, and does not release live caches. |
| **`RecentFilesService`** | `Task<IReadOnlyList<ISearchResult>> GetRecentFilesAsync(IReadOnlyList<string> directories, int limit, int maxAgeMinutes, CancellationToken cancellationToken = default)` | Aggregates recent files across configured folders by querying the host's memory index. |
| **`ExplorerPathService`** | `string? GetLastActivePath()`<br>`IReadOnlyList<string> GetOpenedFolderPaths()` | Retrieves the last directory browsed across File Explorer and all native file dialogs, and the folders currently open there. |
| **`PluginSettingsService`** | `T GetSetting<T>(string pluginId, string key, T defaultValue)`<br>`void SetSetting(string pluginId, string key, object? value)`<br>`bool IsComponentEnabled(string dllName, string componentType, string componentName)`<br>`void NotifySettingChanged(string pluginId, string key, object? value = null)`<br>`event Action<string, string>? SettingChanged`<br>`event Action<string, string, object?>? SettingChangedWithValue`<br>`event Action? ComponentEnablementChanged` | Reads and writes persistent plugin settings and the host's per-component enablement state. `SettingChanged` names what changed; `SettingChangedWithValue` also carries the new value, so a listener does not have to read it back. |
| **`SettingsSearchService`** | `IReadOnlyList<SettingsSearchEntryInfo> GetEntries()`<br>`void Invalidate()` | Reads the host's current searchable settings entries and lets the host refresh its cached snapshot when dynamically contributed entries change. |
| **`SettingsWindowService`** | `bool ShowWindow(string? targetSection = null)`<br>`bool ShowEntry(SettingsSearchEntryInfo? entry)` | Requests that the host show its themed Settings window or navigate directly to a searchable entry, without launching a URI or another process. |
| **`SearchRefreshService`** | `void RefreshIfMatches(Func<string, bool> queryMatches)` | Notifies the host to re-evaluate matching active searches after asynchronous background operations complete. |
| **`UserDataService`** | `string? GetUserDataDirectory()`<br>`string? GetSharedDataDirectory()` | Returns the user-specific data folder and machine-wide shared data directory. Both are nullable: the host may have no such folder resolvable, so check for `null` rather than assuming a path. Only the service writes the shared directory; plugins can read it but must keep their own files in the user folder. |
| **`Logger`** | `void Log(string message, LogLevel level = LogLevel.Info)` | Writes logs to `app.log`, visible in real-time within the Settings log viewer. Lives in the root `Lertaro.PluginSdk` namespace, **not** in `Lertaro.PluginSdk.Services`. |
| **`PluginPromptService`** | `IReadOnlyDictionary<string, object?>? Prompt(string title, IReadOnlyList<PluginConfigField> fields, IReadOnlyDictionary<string, object?>? initialValues = null)` | Displays a lightweight modal input dialog rendered directly from field schemas. Synchronous: it returns the submitted values, or `null` when the user cancelled. Do not `await` it. |
| **`PluginNotificationService`** | `INotificationHandle Show(NotificationRequest request)`<br>`Task<NotificationResult> ShowAsync(NotificationRequest request)`<br>`bool Show(string title, string text, Action? onClick = null)` | Shows a background notification in the host's own windows: a bottom-right card stack (`NotificationPosition.CardStack`) or a one-line notice at the bottom centre (`BottomNotice`). The host clips the requested duration to whatever that position allows, names the sender from the calling assembly so a plugin cannot forge its own attribution, and collapses a card into the notice line while a fullscreen app owns the screen. Never throws into a plugin's background thread, and the handle's task always completes, including when nothing reached the screen; the `bool` overload only reports whether a host served the request. A plugin that needs an answer instead of an announcement uses `PluginMessageBoxService`. The full contract — durations and limits, replacement by `Id`, failure reasons, click semantics, placement and threading — is in [**Notifications**](./notifications). |
| **`PluginMessageBoxService`** | `MessageBoxResult Show(string messageBoxText, string caption = "", MessageBoxButton button = MessageBoxButton.OK, MessageBoxImage icon = MessageBoxImage.None, MessageBoxResult defaultResult = MessageBoxResult.OK)` | Requests a host-owned message box so plugins can use the host's themed UI, with a platform fallback when no host handler is registered. |
| **`ExplorerService`** | `void OpenDirectory(string directoryPath, string? fileNameOrFilePath = null)`<br>`void OpenFolder(string? folderPath)` | Opens the specified directory or locates a file, respecting the host's configured third-party file manager (or Explorer tabs), with fallback to system Explorer. `OpenFolder` is the plain "open this folder, or focus Explorer if there is nothing to open" form and accepts `null`. |

`SettingsSearchService.GetEntries()` returns entries whose indexes are valid only during the current host process. Pass an entry directly to `SettingsWindowService.ShowEntry(...)`; the SDK invokes the host callback and does not construct or launch `lertaro://` URIs.

`HistoryEntry` exposes `Keyword`, `Path`, `Kind`, `Time` (Unix seconds), and `Count` (the number of times the item was opened). `HistoryService.GetHistoryEntries()` returns the entries in most-recently-opened order.

### Component enablement and expensive runtime state

`PluginSettingsService.IsComponentEnabled(...)` reads the host's per-component switch. Components that own directory watchers, background workers, external runtimes, or other expensive state should check it before initializing that state and subscribe to `ComponentEnablementChanged` to start or stop it when the user changes the switch. The method returns `true` when no host callback is registered or the callback fails, so plugins remain usable outside the full host.

## 2. Shell Native File Operations

`Lertaro.PluginSdk.Shell.FileOperations` wraps the native Windows Shell `IFileOperation` COM interface, providing native progress dialogs, conflict prompts, and `Ctrl+Z` undo support:

```csharp
namespace Lertaro.PluginSdk.Shell.FileOperations;

// Batch paste or move as a single atomic Shell operation. `move` carries no default: say which one you mean.
public static class ShellPasteHelper
{
    public static void PasteAsync(
        IReadOnlyList<string> sourcePaths,
        string destinationFolder,
        bool move,
        Action? onCompleted = null);
}

// Recycle bin or permanent deletion. `permanent` likewise has no default.
public static class ShellDeleteHelper
{
    public static void DeleteAsync(IReadOnlyList<string> paths, bool permanent);
}

// Rename one existing file or folder
public static class ShellRenameHelper
{
    public static void RenameAsync(string path, string newName);
}

// Virtual file extraction from drag-and-drop streams
public static class VirtualFileExtractor
{
    public static bool HasVirtualFiles(IDataObject? data);                        // is FileGroupDescriptorW present
    public static List<string> Extract(IDataObject? data, string targetFolder);   // synchronous; the files it wrote
    public static string? ResolveDestination(string targetFolder, string name);   // null on blank input; auto-renames to (2)
}
```

All three operation helpers are **fire-and-forget `void`**, not `Task`: `PasteAsync` takes an optional `onCompleted` callback, and the others report nothing. An empty or blank argument set is ignored rather than throwing.

> [!TIP]
> The helpers marshal themselves onto an STA worker thread that the **SDK** owns and starts inside your own process (`ShellOperationStaWorker` — internal to the SDK, not something you register or configure), so a plugin never has to manage COM apartment threading for a shell operation. Cancellation, and a result to await, are deliberately not offered: the native confirmation and progress dialogs are the interaction, and a plugin that needs to know the outcome should read the destination afterwards.

## 3. Application lifecycle and themed plugin windows

`AppLifecycleService.RequestRestart()` asks the host application to perform an orderly restart. The host starts the replacement process, waits for the current instance to finish its normal shutdown, and then exits; plugins do not need to launch the executable or shut down the host themselves. The method returns `true` when the host accepts the request.

For plugin-owned WPF content, `Lertaro.PluginSdk.Windows.PluginWindow` supplies the host's rounded, themed window frame. Set `ContentHostControl.Content` to the plugin view and add footer buttons through `Footer`. Use `PluginWindowMode.Window` for a normal taskbar window or `PluginWindowMode.Dialog` for a topmost dialog that is hidden from Alt+Tab. An omitted icon uses the host's default application icon.

```csharp
var window = new PluginWindow("My tool", 720, 470, PluginWindowMode.Dialog);
window.ContentHostControl.Content = new MyView();
window.Footer.Children.Add(new Button { Content = "OK", IsDefault = true });
window.ShowDialog();
```

`PluginWindow.ShowFooter` turns the footer row off when a tool has no buttons; an omitted icon argument falls back to the host's default application icon.

## 4. Window, Query, Theme and Preview Infrastructure

| Host Service | Core Signatures | Key Capabilities |
| :--- | :--- | :--- |
| **`SearchWindowService`** | `bool IsWindowVisible()`<br>`void ShowWindow(string? query = null)`<br>`void HideWindow()`<br>`void FocusQueryTextBox()` | Queries and drives the host's search windows from a plugin, including handing it a starting query. |
| **`SearchQueryService`** | `void ChangeQuery(string query, bool requery = false)`<br>`string StripQueryTokens(string query)` | Writes the active window's query, optionally re-running the search, and strips the host's suffix tokens so a plugin sees the plain text the user typed. |
| **`ThemeService`** | `bool IsDarkTheme` | One flag for a plugin that paints its own content. When the host delegate is absent it reads the running WPF application's resources, so a plugin rendered outside the launcher still gets an answer rather than throwing. |
| **`PluginPreviewCache`** | `string Register(string title, string pluginName, Lazy<UserControl> factory, Func<object?>? iconProvider = null)`<br>`PluginPreviewEntry? GetEntry(string key)`<br>`UIElement? GetPreview(string key)` | Registers a plugin-owned preview control lazily and hands the host the key to look it up by, so the control is built the first time it is actually shown rather than at load. |
| **`PreviewActivationSignal`** | `void Begin()`<br>`void End()`<br>`bool IsActive`<br>`event Action? FocusStolen` / `void NotifyFocusStolen()` | Set for as long as an **out-of-process** native handler is actively hosted — the whole viewing session, not just its cold start, because interacting with its rendered content can pop a real top-level window. A nested `Begin`/`End` depth is counted, so providers must pair their calls. *Current host behavior, not a contract:* while the signal is set the search windows stop treating their own deactivation as "the user clicked away". |
| **`PreviewDialogSignal`** | `void NotifyDialogOpened()`<br>`void NotifyDialogClosed()`<br>`event Action? DialogOpened` / `DialogClosed` | Raised by a provider when a native handler's own popup appears (Word's "enter password" prompt for an encrypted file being previewed is the case it exists for). *Current host behavior, not a contract:* the quick window and its preview are hidden while the dialog is up so the dialog can be reached, then restored. |
| **`LocalSendTransferService`** | `void OpenSendWindow(IReadOnlyList<string>? files, string? text)` | Opens the host's LocalSend window pre-loaded with a file list or a text payload, so a plugin can hand off a transfer without owning any of the UI. |
| **`ToolRunService`** | `Func<string, string, Task<string?>>? RunDopusPathsFunc` (host-assigned) | Runs an external tool in the process that can actually be answered by it. The host's keyboard hook is elevated, and an elevated `dopusrt.exe` can never be answered by unelevated Directory Opus (UIPI blocks the reply), while demoting a process needs a privilege the hook's token does not hold. The App runs at the user's own level, so the hook forwards the request there. Read the delegate and treat `null` as unavailable; the caller creates the output file first, because the tool fills in a file that already exists. |
