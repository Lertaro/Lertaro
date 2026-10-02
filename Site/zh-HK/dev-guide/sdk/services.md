# 宿主開放服務

`Lertaro.PluginSdk.Services` 命名空間下提供了一組高效能的靜態基礎設施服務。這些服務對宿主內部包裝的核心演算法、快取與平台介面進行了輕量級封裝，使外掛模組能夠以極簡的程式碼直接複用宿主能力。

## 1. 核心靜態服務一覽

| 宿主服務 | 核心方法與簽章 | 功能說明 |
| :--- | :--- | :--- |
| **`FuzzyMatchService`** | `bool IsMatch(string pattern, string text)`<br>`bool[]? GetHighlightMask(string text, string query)`<br>`double GetMatchScore(string text, string query)` | 運行與宿主完全一致的 fzf 模糊比對引擎，計算字元級的反白布林遮罩（自動支援中文字元拼音多級兜底），並提供用於統一排序的比對品質評分。 |
| **`TranslationService`** | `string Get(string key)`<br>`bool TryGet(string key, out string result)`<br>`string Format(string key, params object[] args)`<br>`string GetCurrentCulture()`<br>`IReadOnlyList<string> GetSupportedCultures(Assembly assembly)`<br>`Dictionary<string, string> LoadEmbeddedTranslations(Assembly assembly, string cultureKey, string typeName)`<br>`event Action<string>? CultureChanged` | 多語言動態剖析與執行階段變更廣播。`GetCurrentCulture()` 返回使用者在設定中心顯式選取的介面語言代碼（如 `"zh-HK"`）；訂閱 `CultureChanged` 可在介面語言切換時動態重新整理內部狀態或重載字典。`TryGet` 回答某個鍵是否解析成功，而 `Get` 會退回可見的 `[key]` 佔位文字而不是擲出例外。`GetSupportedCultures(assembly)` 列出某個組件的內嵌資源所涵蓋的語系；`LoadEmbeddedTranslations(assembly, cultureKey, typeName)` 則返回單一語系的字典。 |
| **`IconService`** | `ImageSource? GetIcon(string path, bool isDir)`<br>`ImageSource? GetThumbnail(string path, int size)`<br>`ImageSource? GetIconFromCacheOnly(string path, bool isDir, out bool needsLoad)` | 帶記憶體與磁碟快取的 Windows Shell 檔案圖示與縮圖擷取服務。`GetIconFromCacheOnly` 完全不會碰觸 Shell：它只返回已經快取的內容，並透過 `needsLoad` 回報是否仍需要真正載入一次，列表得以先立即繪製、事後再補齊圖示，靠的就是這個機制。 |
| **`FavoritesService`** | `IEnumerable<FavoriteItem> GetFavorites()`<br>`bool IsFavorite(string path)`<br>`bool TryAddFavorite(FavoriteItem favorite)` | 讀取收藏清單、檢查路徑是否已登記，並透過宿主橋接新增收藏項目。 |
| **`HistoryService`** | `IEnumerable<HistoryEntry> GetHistoryEntries()` | 讀取搜尋記錄項目，按最近開啟時間降序排列，包含關聯的搜尋關鍵字、檔案類型與單筆記錄的使用次數。同一實體路徑最多出現一次，並歸屬於最近一次開啟它時使用的關鍵字。 |
| **`FileMetadataService`** | `Task<IReadOnlyDictionary<string, FileMetadata>> GetMetadataAsync(IReadOnlyList<string> paths)` | 批次查詢外部路徑的實體檔案大小與時間戳記（僅用於查詢未出現在當前搜尋結果集中的外部路徑）。 |
| **`DirectoryIndexerService`** | `void RegisterDirectory(string pluginId, string directoryPath, bool recursive = true, string filterPattern = "*")`<br>`void UnregisterDirectories(string pluginId)`<br>`IDisposable WatchDirectories(string pluginId, Action onChanged)`<br>`IDisposable WatchDirectories(string pluginId, Action<IReadOnlyList<string>> onChanged)`<br>`void NotifyDirectoryChanged(string pluginId)`<br>`void NotifyDirectoryChanged(string pluginId, IReadOnlyList<string> changedDirectories)`<br>`Task<List<ISearchResult>> SearchDirectoriesAsync(string pluginId, string query, CancellationToken token = default)`<br>`IAsyncEnumerable<ISearchResult> EnumerateDirectoryAsync(string directoryPath, bool recursive = false, string filterPattern = "*", int limit = 0, CancellationToken token = default)` | 允許外掛模組向宿主註冊自訂目錄，以進行基於宿主索引的搜尋和變更監聽。目錄列舉只讀取宿主檔案索引並以串流返回；未被索引涵蓋的目錄會返回空白序列，因此呼叫方必須確保目錄由已設定的本機磁碟機、網路或資料夾索引涵蓋。宿主不會直接掃描檔案系統。請留意這個刻意保留的不對稱：`RegisterDirectory` 預設遞迴，而 `EnumerateDirectoryAsync` 預設不遞迴。監聽通知會經過防抖處理，並可攜帶受影響目錄；空白清單表示宿主無法確定更窄的範圍。 |
| **`MemoryMaintenanceService`** | `void RequestTrim()` | 外掛程式完成一段臨時記憶體分配密集的後台工作後，請求宿主延遲執行工作集維護。請求可能會合併或忽略，不會釋放仍在使用的快取。 |
| **`RecentFilesService`** | `Task<IReadOnlyList<ISearchResult>> GetRecentFilesAsync(IReadOnlyList<string> directories, int limit, int maxAgeMinutes, CancellationToken cancellationToken = default)` | 透過查詢宿主的記憶體索引，彙整各已設定目錄中的近期檔案。 |
| **`ExplorerPathService`** | `string? GetLastActivePath()`<br>`IReadOnlyList<string> GetOpenedFolderPaths()` | 獲取使用者最近一次在檔案總管或任意應用程式的檔案選取對話方塊中瀏覽過的活動目錄路徑，以及該處目前開啟的資料夾。 |
| **`PluginSettingsService`** | `T GetSetting<T>(string pluginId, string key, T defaultValue)`<br>`void SetSetting(string pluginId, string key, object? value)`<br>`bool IsComponentEnabled(string dllName, string componentType, string componentName)`<br>`void NotifySettingChanged(string pluginId, string key, object? value = null)`<br>`event Action<string, string>? SettingChanged`<br>`event Action<string, string, object?>? SettingChangedWithValue`<br>`event Action? ComponentEnablementChanged` | 讀取與寫入外掛模組持久化的設定項目，以及宿主儲存的元件級啟用狀態。`SettingChanged` 會指出變更的是什麼；`SettingChangedWithValue` 還帶著新的值，因此監聽端不必再自行讀回。 |
| **`SettingsSearchService`** | `IReadOnlyList<SettingsSearchEntryInfo> GetEntries()`<br>`void Invalidate()` | 讀取宿主目前可搜尋的設定項目，並在動態提供的項目發生變更時通知宿主重新整理快取快照。 |
| **`SettingsWindowService`** | `bool ShowWindow(string? targetSection = null)`<br>`bool ShowEntry(SettingsSearchEntryInfo? entry)` | 請求宿主顯示主題化設定視窗，或直接跳轉到可搜尋的設定項目，不啟動 URI 或其他程序。 |
| **`SearchRefreshService`** | `void RefreshIfMatches(Func<string, bool> queryMatches)` | 用於非同步即時計算來源完成後台資料獲取後，通知宿主原地重跑當前比對的搜尋查詢並重新整理檢視。 |
| **`UserDataService`** | `string? GetUserDataDirectory()`<br>`string? GetSharedDataDirectory()` | 獲取當前使用者的專屬資料目錄（存放私有設定）與機器級全域共用資料目錄（共用 Python/Node 執行階段）。兩者都可為 `null`：宿主也許解析不到這類資料夾，因此請檢查 `null`，而不是假定一定有路徑。 |
| **`Logger`** | `void Log(string message, LogLevel level = LogLevel.Info)` | 統一輸出記錄至 `app.log`，並在設定中心的即時記錄檢視器中同步呈現。它位於基底命名空間 `Lertaro.PluginSdk`，**不是** `Lertaro.PluginSdk.Services`。 |
| **`PluginPromptService`** | `IReadOnlyDictionary<string, object?>? Prompt(string title, IReadOnlyList<PluginConfigField> fields, IReadOnlyDictionary<string, object?>? initialValues = null)` | 快顯基於 Schema 自動轉譯的小型強制回應輸入對話方塊，向使用者請求一次性輸入。此方法為同步：它返回使用者提交的值，使用者取消時返回 `null`。請勿對它 `await`。 |
| **`PluginNotificationService`** | `INotificationHandle Show(NotificationRequest request)`<br>`Task<NotificationResult> ShowAsync(NotificationRequest request)`<br>`bool Show(string title, string text, Action? onClick = null)` | 由宿主自身的視窗顯示背景通知：右下角的卡片堆（`NotificationPosition.CardStack`），或螢幕下方置中的一行提示（`BottomNotice`）。宿主會把要求的時長裁剪到該位置允許的範圍，依呼叫端組件標記發送者（外掛程式無法偽造自己的來源），並在獨佔全螢幕應用佔用螢幕時把卡片降級成那一行提示。它不會把例外拋進外掛程式的背景執行緒，句柄的工作也必然完成，包括什麼都沒顯示出來的情況；`bool` 多載只表示宿主是否受理了請求。要取得回覆而非單純告知時，改用 `PluginMessageBoxService`。完整契約——時長與上限、以 `Id` 取代、失敗原因、點擊語意、放置與執行緒——見[**通知卡片**](./notifications)。 |
| **`PluginMessageBoxService`** | `MessageBoxResult Show(string messageBoxText, string caption = "", MessageBoxButton button = MessageBoxButton.OK, MessageBoxImage icon = MessageBoxImage.None, MessageBoxResult defaultResult = MessageBoxResult.OK)` | 請求由宿主顯示訊息方塊，讓外掛模組使用宿主的主題化介面；未註冊宿主處理器時回退至系統訊息方塊。 |
| **`ExplorerService`** | `void OpenDirectory(string directoryPath, string? fileNameOrFilePath = null)`<br>`void OpenFolder(string? folderPath)` | 開啟指定資料夾或定位指定檔案，遵循宿主配置的第三方檔案管理員（或檔案總管分頁），未配置時回退至系統檔案總管。`OpenFolder` 就是單純「開啟這個資料夾，若沒有資料夾可開啟則改為聚焦檔案總管」的形式，而且接受 `null`。 |

`SettingsSearchService.GetEntries()` 回傳的項目索引只在目前宿主程序中有效。將項目直接傳給 `SettingsWindowService.ShowEntry(...)`，SDK 會呼叫宿主回呼，不會建立或啟動 `lertaro://` URI。

`HistoryEntry` 提供 `Keyword`、`Path`、`Kind`、`Time`（Unix 秒）和 `Count`（項目被開啟的次數）欄位。`HistoryService.GetHistoryEntries()` 按最近開啟順序返回記錄。

### 元件啟用狀態與高成本執行階段

`PluginSettingsService.IsComponentEnabled(...)` 用於讀取宿主儲存的元件級開關。擁有目錄監聽器、背景工作執行緒、外部執行階段或其他高成本狀態的元件，應在初始化這些狀態前先檢查開關，並訂閱 `ComponentEnablementChanged`，在使用者切換開關後啟動或停止對應執行階段。如果宿主沒有註冊回調或回調失敗，此方法會返回 `true`，確保外掛模組在未接入完整宿主時仍可使用。

## 2. Shell 原生檔案操作封裝

`Lertaro.PluginSdk.Shell.FileOperations` 封裝了 Windows Shell 原生的 `IFileOperation` 介面。外掛模組執行檔案移動、複製與刪除時，使用者將獲得與檔案總管完全一致的原生進度對話方塊、衝突替換提示與 `Ctrl+Z` 復原支援：

```csharp
namespace Lertaro.PluginSdk.Shell.FileOperations;

// 批次貼上或移動，合併為單次 Shell 操作。`move` 沒有預設值：請自己說清楚要哪一個
public static class ShellPasteHelper
{
    public static void PasteAsync(
        IReadOnlyList<string> sourcePaths,
        string destinationFolder,
        bool move,
        Action? onCompleted = null);
}

// 安全放入資源回收筒或永久刪除。`permanent` 同樣沒有預設值
public static class ShellDeleteHelper
{
    public static void DeleteAsync(IReadOnlyList<string> paths, bool permanent);
}

// 重新命名單一已存在的檔案或資料夾
public static class ShellRenameHelper
{
    public static void RenameAsync(string path, string newName);
}

// 虛擬檔案與網頁拖曳串流擷取
public static class VirtualFileExtractor
{
    public static bool HasVirtualFiles(IDataObject? data);                        // 是否存在 FileGroupDescriptorW
    public static List<string> Extract(IDataObject? data, string targetFolder);   // 同步返回；給出它實際寫出的檔案
    public static string? ResolveDestination(string targetFolder, string name);   // 輸入為空白時返回 null；重名自動加 (2)
}
```

這三個操作輔助類別全都是**即發即忘的 `void`**，而不是 `Task`：`PasteAsync` 接受選填的 `onCompleted` 回呼，其餘方法則完全不回報任何結果。引數集合為空或純空白時會被忽略，而不會擲出例外。

> [!TIP]
> 這些輔助方法會自行封送到一個由 **SDK** 擁有、並在你自己的程序內啟動的 STA 背景工作執行緒（`ShellOperationStaWorker`——它屬於 SDK 內部，不是你要註冊或設定的東西），因此外掛模組執行 Shell 操作時無需自行管理 COM 執行緒套間。取消，以及可供 `await` 的結果，都是刻意不提供的：原生的確認與進度對話方塊就是互動本身，需要知道結果的外掛模組請在事後自行讀取目標位置。

## 3. 應用程式生命週期與主題化外掛模組視窗

`AppLifecycleService.RequestRestart()` 會請求宿主應用程式執行優雅重新啟動。宿主會啟動替代程序，等待目前執行個體完成正常退出後再結束；外掛模組無需自行啟動可執行檔或關閉宿主。宿主接受請求時此方法會回傳 `true`。

對於外掛模組自有的 WPF 內容，`Lertaro.PluginSdk.Windows.PluginWindow` 提供統一的圓角主題視窗框架。將外掛模組視圖指派給 `ContentHostControl.Content`，並透過 `Footer` 加入底部按鈕。一般工作列視窗使用 `PluginWindowMode.Window`；需要置頂且從 Alt+Tab 隱藏的對話方塊使用 `PluginWindowMode.Dialog`。不傳入圖示時會使用宿主的預設應用程式圖示。

```csharp
var window = new PluginWindow("我的工具", 720, 470, PluginWindowMode.Dialog);
window.ContentHostControl.Content = new MyView();
window.Footer.Children.Add(new Button { Content = "確定", IsDefault = true });
window.ShowDialog();
```

`PluginWindow.ShowFooter` 用於在工具沒有任何按鈕時關閉底部按鈕列；省略圖示參數時則退回宿主預設的應用程式圖示。

## 4. 視窗、查詢、主題與預覽基礎設施

| 宿主服務 | 核心方法與簽章 | 功能說明 |
| :--- | :--- | :--- |
| **`SearchWindowService`** | `bool IsWindowVisible()`<br>`void ShowWindow(string? query = null)`<br>`void HideWindow()`<br>`void FocusQueryTextBox()` | 讓外掛模組查詢並驅動宿主的搜尋視窗，包括交付一個起始查詢字串。 |
| **`SearchQueryService`** | `void ChangeQuery(string query, bool requery = false)`<br>`string StripQueryTokens(string query)` | 寫入目前視窗的查詢字串，並可選擇是否重新執行搜尋；同時剝除宿主的後綴 Token，讓外掛模組看到使用者實際打出的純文字。 |
| **`ThemeService`** | `bool IsDarkTheme` | 供自行繪製內容的外掛模組使用的單一旗標。宿主委派不存在時，它會改讀正在執行的 WPF 應用程式資源，因此在啟動器之外轉譯的外掛模組仍能取得答案，而不是擲出例外。 |
| **`PluginPreviewCache`** | `string Register(string title, string pluginName, Lazy<UserControl> factory, Func<object?>? iconProvider = null)`<br>`PluginPreviewEntry? GetEntry(string key)`<br>`UIElement? GetPreview(string key)` | 以延遲方式註冊外掛模組自有的預覽控制項，並把宿主據以查閱的金鑰交給它，於是控制項是在第一次真正要顯示時才建立，而不是在載入時。 |
| **`PreviewActivationSignal`** | `void Begin()`<br>`void End()`<br>`bool IsActive`<br>`event Action? FocusStolen` / `void NotifyFocusStolen()` | 在**跨程序**的原生處理內容被實際承載期間一直保持設定——涵蓋整個瀏覽會話而不只是它的冷啟動，因為與它轉譯出的內容互動可能彈出真正的頂層視窗。巢狀的 `Begin`/`End` 深度會被計數，因此提供者必須成對呼叫。*目前為宿主行為，並非契約：* 該訊號生效期間，搜尋視窗不再把自己的失焦當成「使用者點到別處去了」。 |
| **`PreviewDialogSignal`** | `void NotifyDialogOpened()`<br>`void NotifyDialogClosed()`<br>`event Action? DialogOpened` / `DialogClosed` | 由提供者在原生處理內容自己的彈出視窗出現時觸發（它存在的理由，就是預覽中的加密檔在 Word 裡跳出「輸入密碼」提示）。*目前為宿主行為，並非契約：* 對話方塊顯示期間，快速視窗與它的預覽會被隱藏，好讓那個對話方塊可以被操作，隨後再還原兩者。 |
| **`LocalSendTransferService`** | `void OpenSendWindow(IReadOnlyList<string>? files, string? text)` | 開啟宿主的 LocalSend 視窗並預先載入檔案清單或文字內容，讓外掛模組不必擁有任何介面就能交接一次傳輸。 |
| **`ToolRunService`** | `Func<string, string, Task<string?>>? RunDopusPathsFunc`（宿主指派） | 在真正能回應它的那個程序層級中執行外部工具。宿主的鍵盤掛鉤是提權的，而被提權的 `dopusrt.exe` 永遠不可能由未提權的 Directory Opus 回應（UIPI 會擋下回應），而降權一個程序需要掛鉤權杖所沒有的特權。App 以使用者自身的權限層級運行，因此掛鉤會把請求轉送到那裡。請讀取這個委派並將 `null` 視為不可用；呼叫端必須先建立輸出檔案，因為該工具只會填入已經存在的檔案。 |
