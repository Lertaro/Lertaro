# 宿主開放服務

`Lertaro.PluginSdk.Services` 命名空間下提供了一組高效能的靜態基礎設施服務。這些服務對宿主內部包裝的核心演算法、快取與平台介面進行了輕量級封裝，使外掛模組能夠以極簡的程式碼直接複用宿主能力。

## 1. 核心靜態服務一覽

| 宿主服務 | 核心方法與簽章 | 功能說明 |
| :--- | :--- | :--- |
| **`FuzzyMatchService`** | `bool IsMatch(string pattern, string text)`<br>`bool[]? GetHighlightMask(string text, string query)`<br>`double GetMatchScore(string text, string query)` | 運行與宿主完全一致的 fzf 模糊比對引擎，計算字元級的反白布林遮罩（自動支援中文字元拼音多級兜底），並提供用於統一排序的比對品質評分。 |
| **`TranslationService`** | `string Get(string key)`<br>`string Format(string key, params object[] args)`<br>`void LoadEmbeddedTranslations(...)`<br>`string GetCurrentCulture()`<br>`event Action<string>? CultureChanged` | 多語言動態剖析與執行階段變更廣播。`GetCurrentCulture()` 返回使用者在設定中心顯式選取的介面語言代碼（如 `"zh-HK"`）；訂閱 `CultureChanged` 可在介面語言切換時動態重新整理內部狀態或重載字典。 |
| **`IconService`** | `ImageSource? GetIcon(string path, bool isDir)`<br>`ImageSource? GetThumbnail(string path, int size)` | 帶記憶體與磁碟快取的 Windows Shell 檔案圖示與縮圖擷取服務。 |
| **`FavoritesService`** | `IReadOnlyList<FavoriteItem> GetFavorites()`<br>`bool IsFavorite(string path)`<br>`bool TryAddFavorite(FavoriteItem favorite)` | 讀取收藏清單、檢查路徑是否已登記，並透過宿主橋接新增收藏項目。 |
| **`HistoryService`** | `IReadOnlyList<HistoryEntry> GetHistoryEntries()` | 讀取搜尋記錄項目，按最近開啟時間降序排列，包含關聯的搜尋關鍵字與檔案類型。 |
| **`FileMetadataService`** | `Task<IReadOnlyDictionary<string, FileMetadata>> GetMetadataAsync(IEnumerable<string> paths)` | 批次查詢外部路徑的實體檔案大小與時間戳記（僅用於查詢未出現在當前搜尋結果集中的外部路徑）。 |
| **`DirectoryIndexerService`** | `void RegisterDirectory(string pluginId, string path, bool recursive, string? filterPattern)`<br>`IDisposable WatchDirectories(string pluginId, Action onChanged)`<br>`IDisposable WatchDirectories(string pluginId, Action<IReadOnlyList<string>> onChanged)`<br>`IAsyncEnumerable<ISearchResult> EnumerateDirectoryAsync(...)` | 允許外掛模組向後台服務註冊專屬自訂目錄以進行自動索引與變更監聽。目錄列舉會優先使用宿主索引並以串流返回；索引未涵蓋時由宿主負責回退處理。監聽通知會經過防抖處理，並可攜帶受影響目錄；空白清單表示宿主無法確定更窄的範圍。 |
| **`MemoryMaintenanceService`** | `void RequestTrim()` | 外掛程式完成一段臨時記憶體分配密集的後台工作後，請求宿主延遲執行工作集維護。請求可能會合併或忽略，不會釋放仍在使用的快取。 |
| **`RecentFilesService`** | `Task<IReadOnlyList<ISearchResult>> GetRecentFilesAsync(IEnumerable<string> directories, int limit, int maxAgeMinutes, CancellationToken token)` | 利用記憶體索引快速擷取指定目錄清單下的最新修改檔案集合（毫秒級應答，不產生實體磁碟 I/O）。 |
| **`ExplorerPathService`** | `string? GetLastActivePath()` | 獲取使用者最近一次在檔案總管或任意應用程式的檔案選取對話方塊中瀏覽過的活動目錄路徑。 |
| **`PluginSettingsService`** | `T GetSetting<T>(string pluginId, string key, T defaultValue)`<br>`bool IsComponentEnabled(string dllName, string componentType, string componentName)`<br>`event Action<string, string>? SettingChanged`<br>`event Action? ComponentEnablementChanged` | 讀取外掛模組持久化的設定項目，以及宿主儲存的元件級啟用狀態。 |
| **`SettingsSearchService`** | `IReadOnlyList<SettingsSearchEntryInfo> GetEntries()`<br>`void Invalidate()` | 讀取宿主目前可搜尋的設定項目，並在動態提供的項目發生變更時通知宿主重新整理快取快照。 |
| **`SettingsWindowService`** | `bool ShowWindow(string? targetSection = null)`<br>`bool ShowEntry(SettingsSearchEntryInfo? entry)` | 請求宿主顯示主題化設定視窗，或直接跳轉到可搜尋的設定項目，不啟動 URI 或其他程序。 |
| **`SearchRefreshService`** | `void RefreshIfMatches(Func<string, bool> queryMatches)` | 用於非同步即時計算來源完成後台資料獲取後，通知宿主原地重跑當前比對的搜尋查詢並重新整理檢視。 |
| **`UserDataService`** | `string GetUserDataDirectory()`<br>`string GetSharedDataDirectory()` | 獲取當前使用者的專屬資料目錄（存放私有設定）與機器級全域共用資料目錄（共用 Python/Node 執行階段）。 |
| **`Logger`** | `void Log(string message, LogLevel level = LogLevel.Info)` | 統一輸出記錄至 `app.log`，並在設定中心的即時記錄檢視器中同步呈現。 |
| **`PluginPromptService`** | `Task<Dictionary<string, object?>?> Prompt(string title, IEnumerable<PluginConfigField> fields, ...)` | 快顯基於 Schema 自動轉譯的小型強制回應輸入對話方塊，向使用者請求一次性輸入。 |
| **`PluginMessageBoxService`** | `MessageBoxResult Show(string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon, MessageBoxResult defaultResult)` | 請求由宿主顯示訊息方塊，讓外掛模組使用宿主的主題化介面；未註冊宿主處理器時回退至系統訊息方塊。 |
| **`ExplorerService`** | `void OpenDirectory(string directoryPath, string? fileNameOrFilePath = null)` | 開啟指定資料夾或定位指定檔案，遵循宿主配置的第三方檔案管理員（或檔案總管分頁），未配置時回退至系統檔案總管。 |

`SettingsSearchService.GetEntries()` 回傳的項目索引只在目前宿主程序中有效。將項目直接傳給 `SettingsWindowService.ShowEntry(...)`，SDK 會呼叫宿主回呼，不會建立或啟動 `lertaro://` URI。

### 元件啟用狀態與高成本執行階段

`PluginSettingsService.IsComponentEnabled(...)` 用於讀取宿主儲存的元件級開關。擁有目錄監聽器、背景工作執行緒、外部執行階段或其他高成本狀態的元件，應在初始化這些狀態前先檢查開關，並訂閱 `ComponentEnablementChanged`，在使用者切換開關後啟動或停止對應執行階段。如果宿主沒有註冊回調或回調失敗，此方法會返回 `true`，確保外掛模組在未接入完整宿主時仍可使用。

## 2. Shell 原生檔案操作封裝

`Lertaro.PluginSdk.Shell.FileOperations` 封裝了 Windows Shell 原生的 `IFileOperation` 介面。外掛模組執行檔案移動、複製與刪除時，使用者將獲得與檔案總管完全一致的原生進度對話方塊、衝突替換提示與 `Ctrl+Z` 復原支援：

```csharp
namespace Lertaro.PluginSdk.Shell.FileOperations;

// 批次貼上或移動（合併為單次 Shell 操作）
public static class ShellPasteHelper
{
    public static void PasteAsync(
        IEnumerable<string> sourcePaths,
        string destinationFolder,
        bool move = false,
        Action? onCompleted = null);
}

// 安全放入資源回收筒或永久刪除
public static class ShellDeleteHelper
{
    public static void DeleteAsync(IEnumerable<string> paths, bool permanent = false);
}

// 虛擬檔案與網頁拖曳串流擷取
public static class VirtualFileExtractor
{
    public static bool HasVirtualFiles(IDataObject dataObject);
    public static Task<IReadOnlyList<string>> Extract(IDataObject dataObject, string targetFolder);
    public static string ResolveDestination(string folder, string name); // 重名自動加 (2) 規則
}
```

> [!TIP]
> 上述 Shell 非同步幫助類別均自動運行在 SDK 獨立的專用 STA 背景工作執行緒（`ShellOperationStaWorker`）中，外掛模組呼叫時無需自行建立 STA 執行緒套間。

## 3. 應用程式生命週期與主題化外掛模組視窗

`AppLifecycleService.RequestRestart()` 會請求宿主應用程式執行優雅重新啟動。宿主會啟動替代程序，等待目前執行個體完成正常退出後再結束；外掛模組無需自行啟動可執行檔或關閉宿主。宿主接受請求時此方法會回傳 `true`。

對於外掛模組自有的 WPF 內容，`Lertaro.PluginSdk.Windows.PluginWindow` 提供統一的圓角主題視窗框架。將外掛模組視圖指派給 `ContentHostControl.Content`，並透過 `Footer` 加入底部按鈕。一般工作列視窗使用 `PluginWindowMode.Window`；需要置頂且從 Alt+Tab 隱藏的對話方塊使用 `PluginWindowMode.Dialog`。不傳入圖示時會使用宿主的預設應用程式圖示。

```csharp
var window = new PluginWindow("我的工具", 720, 470, PluginWindowMode.Dialog);
window.ContentHostControl.Content = new MyView();
window.Footer.Children.Add(new Button { Content = "確定", IsDefault = true });
window.ShowDialog();
```
