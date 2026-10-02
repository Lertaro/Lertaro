# 宿主開放服務

`Lertaro.PluginSdk.Services` 命名空間下提供了一組高效能的靜態基礎設施服務。這些服務對宿主內部包裝的核心演算法、快取與平台介面進行了輕量級封裝，使外掛模組能夠以極簡的程式碼直接複用宿主能力。

## 1. 核心靜態服務一覽

| 宿主服務 | 核心方法與簽章 | 功能說明 |
| :--- | :--- | :--- |
| **`FuzzyMatchService`** | `bool IsMatch(string pattern, string text)`<br>`bool[]? GetHighlightMask(string text, string query)`<br>`double GetMatchScore(string text, string query)` | 執行與宿主完全一致的 fzf 模糊比對引擎，計算字元級的反白布林遮罩（自動支援中文字元拼音多級兜底），並提供用於統一排序的比對品質評分。 |
| **`TranslationService`** | `string Get(string key)`<br>`bool TryGet(string key, out string result)`<br>`string Format(string key, params object[] args)`<br>`string GetCurrentCulture()`<br>`IReadOnlyList<string> GetSupportedCultures(Assembly assembly)`<br>`Dictionary<string, string> LoadEmbeddedTranslations(Assembly assembly, string cultureKey, string typeName)`<br>`event Action<string>? CultureChanged` | 多語言動態剖析與執行階段變更廣播。`GetCurrentCulture()` 返回使用者在設定中心顯式選取的介面語言代碼（如 `"zh-TW"`）；訂閱 `CultureChanged` 可在介面語言切換時動態重新整理內部狀態或重載字典。`TryGet` 會回答某個鍵是否已解析成功，而 `Get` 會回退成一個可見的 `[key]` 佔位符而非擲出例外。`GetSupportedCultures(assembly)` 列出某個組件的內嵌資源涵蓋了哪些語系，`LoadEmbeddedTranslations(assembly, cultureKey, typeName)` 則返回單一語系的字典。 |
| **`IconService`** | `ImageSource? GetIcon(string path, bool isDir)`<br>`ImageSource? GetThumbnail(string path, int size)`<br>`ImageSource? GetIconFromCacheOnly(string path, bool isDir, out bool needsLoad)` | 帶記憶體與磁碟快取的 Windows Shell 檔案圖示與縮圖擷取服務。`GetIconFromCacheOnly` 絕不碰觸 Shell：它返回已快取的內容，並透過 `needsLoad` 回報是否仍需要一次真正的載入，這就是一個清單能立即繪製、事後再補上圖示的做法。 |
| **`FavoritesService`** | `IEnumerable<FavoriteItem> GetFavorites()`<br>`bool IsFavorite(string path)`<br>`bool TryAddFavorite(FavoriteItem favorite)` | 讀取收藏清單、檢查路徑是否已登記，並透過主機橋接新增收藏項目。 |
| **`HistoryService`** | `IEnumerable<HistoryEntry> GetHistoryEntries()` | 讀取搜尋歷程記錄項目，按最近開啟時間降序排列，包含關聯的搜尋關鍵字、檔案類型與單筆記錄的使用次數。同一實體路徑最多出現一次，並歸屬於最近一次開啟它時使用的關鍵字。 |
| **`FileMetadataService`** | `Task<IReadOnlyDictionary<string, FileMetadata>> GetMetadataAsync(IReadOnlyList<string> paths)` | 批次查詢外部路徑的實體檔案大小與時間戳記（僅用於查詢未出現在目前搜尋結果集中的外部路徑）。 |
| **`DirectoryIndexerService`** | `void RegisterDirectory(string pluginId, string directoryPath, bool recursive = true, string filterPattern = "*")`<br>`void UnregisterDirectories(string pluginId)`<br>`IDisposable WatchDirectories(string pluginId, Action onChanged)`<br>`IDisposable WatchDirectories(string pluginId, Action<IReadOnlyList<string>> onChanged)`<br>`void NotifyDirectoryChanged(string pluginId)`<br>`void NotifyDirectoryChanged(string pluginId, IReadOnlyList<string> changedDirectories)`<br>`Task<List<ISearchResult>> SearchDirectoriesAsync(string pluginId, string query, CancellationToken token = default)`<br>`IAsyncEnumerable<ISearchResult> EnumerateDirectoryAsync(string directoryPath, bool recursive = false, string filterPattern = "*", int limit = 0, CancellationToken token = default)` | 允許外掛模組向主機註冊自訂目錄，以進行基於主機索引的搜尋和變更監聽。目錄列舉只讀取主機檔案索引並以串流返回；未被索引涵蓋的目錄會返回空白序列，因此呼叫方必須確保目錄由已設定的本機磁碟機、網路或資料夾索引涵蓋。主機不會直接掃描檔案系統。請留意這個刻意為之的不對稱：`RegisterDirectory` 預設遞迴，而 `EnumerateDirectoryAsync` 預設不遞迴。監聽通知會經過防抖處理，並可攜帶受影響目錄；空白清單表示主機無法確定更窄的範圍。 |
| **`MemoryMaintenanceService`** | `void RequestTrim()` | 外掛模組完成一段臨時記憶體配置密集的背景工作後，請求主機延遲執行工作集維護。請求可能會合併或忽略，不會釋放仍在使用的快取。 |
| **`RecentFilesService`** | `Task<IReadOnlyList<ISearchResult>> GetRecentFilesAsync(IReadOnlyList<string> directories, int limit, int maxAgeMinutes, CancellationToken cancellationToken = default)` | 透過查詢宿主的記憶體索引，彙整已設定資料夾中的近期檔案。 |
| **`ExplorerPathService`** | `string? GetLastActivePath()`<br>`IReadOnlyList<string> GetOpenedFolderPaths()` | 獲取使用者最近一次在檔案總管或任意應用程式的檔案選取對話方塊中瀏覽過的活動目錄路徑，以及該處目前已開啟的資料夾。 |
| **`PluginSettingsService`** | `T GetSetting<T>(string pluginId, string key, T defaultValue)`<br>`void SetSetting(string pluginId, string key, object? value)`<br>`bool IsComponentEnabled(string dllName, string componentType, string componentName)`<br>`void NotifySettingChanged(string pluginId, string key, object? value = null)`<br>`event Action<string, string>? SettingChanged`<br>`event Action<string, string, object?>? SettingChangedWithValue`<br>`event Action? ComponentEnablementChanged` | 讀取並寫入外掛模組持久化的設定項目，以及宿主儲存的元件級啟用狀態。`SettingChanged` 會指出變更了什麼；`SettingChangedWithValue` 還會攜帶新的值，因此監聽端不必再把它讀回來。 |
| **`SettingsSearchService`** | `IReadOnlyList<SettingsSearchEntryInfo> GetEntries()`<br>`void Invalidate()` | 讀取主機目前可搜尋的設定項目，並在動態提供的項目發生變更時通知主機重新整理快取快照。 |
| **`SettingsWindowService`** | `bool ShowWindow(string? targetSection = null)`<br>`bool ShowEntry(SettingsSearchEntryInfo? entry)` | 請求主機顯示佈景主題化設定視窗，或直接跳轉到可搜尋的設定項目，不啟動 URI 或其他程序。 |
| **`SearchRefreshService`** | `void RefreshIfMatches(Func<string, bool> queryMatches)` | 用於非同步即時計算來源完成背景資料獲取後，通知宿主原地重跑目前比對的搜尋查詢並重新整理檢視。 |
| **`UserDataService`** | `string? GetUserDataDirectory()`<br>`string? GetSharedDataDirectory()` | 獲取目前使用者的專屬資料目錄（存放私有設定）與機器級全域共用資料目錄（共用 Python/Node 執行階段）。兩者都可為 `null`：宿主也許解析不到這類資料夾，請檢查 `null`，不要假設一定有路徑。 |
| **`Logger`** | `void Log(string message, LogLevel level = LogLevel.Info)` | 統一輸出記錄至 `app.log`，並在設定中心的即時記錄檢視器中同步呈現。它位於根命名空間 `Lertaro.PluginSdk`，**不是** `Lertaro.PluginSdk.Services`。 |
| **`PluginPromptService`** | `IReadOnlyDictionary<string, object?>? Prompt(string title, IReadOnlyList<PluginConfigField> fields, IReadOnlyDictionary<string, object?>? initialValues = null)` | 快顯基於 Schema 自動轉譯的小型強制回應輸入對話方塊，向使用者請求一次性輸入。同步執行：它返回提交的值，或在使用者取消時返回 `null`。不要對它使用 `await`。 |
| **`PluginNotificationService`** | `INotificationHandle Show(NotificationRequest request)`<br>`Task<NotificationResult> ShowAsync(NotificationRequest request)`<br>`bool Show(string title, string text, Action? onClick = null)` | 由宿主自身的視窗顯示背景通知：右下角的卡片堆（`NotificationPosition.CardStack`），或螢幕下方置中的一行提示（`BottomNotice`）。宿主會把要求的時長裁剪到該位置允許的範圍，依呼叫端組元標記發送者（外掛程式無法偽造自己的來源），並在獨佔全螢幕應用佔用螢幕時把卡片降級成那一行提示。它不會把例外拋進外掛程式的背景執行緒，句柄的工作也必然完成，包括什麼都沒顯示出來的情況；`bool` 多載只表示宿主是否受理了請求。要取得回覆而非單純告知時，改用 `PluginMessageBoxService`。完整的契約——時長與上限、依 `Id` 取代、失敗原因、點擊語意、位置與執行緒——都在[**通知**](./notifications)。 |
| **`PluginMessageBoxService`** | `MessageBoxResult Show(string messageBoxText, string caption = "", MessageBoxButton button = MessageBoxButton.OK, MessageBoxImage icon = MessageBoxImage.None, MessageBoxResult defaultResult = MessageBoxResult.OK)` | 請求由宿主顯示訊息方塊，讓外掛模組使用宿主的主題化介面；未註冊宿主處理器時回退至系統訊息方塊。 |
| **`ExplorerService`** | `void OpenDirectory(string directoryPath, string? fileNameOrFilePath = null)`<br>`void OpenFolder(string? folderPath)` | 開啟指定資料夾或定位指定檔案，遵循宿主配置的第三方檔案管理員（或檔案總管分頁），未配置時回退至系統檔案總管。`OpenFolder` 是最單純的那種「開啟這個資料夾，若沒有東西可開啟就聚焦檔案總管」的形式，並可接受 `null`。 |

`SettingsSearchService.GetEntries()` 回傳的項目索引只在目前主機程序中有效。將項目直接傳給 `SettingsWindowService.ShowEntry(...)`，SDK 會呼叫主機回呼，不會建立或啟動 `lertaro://` URI。

`HistoryEntry` 提供 `Keyword`、`Path`、`Kind`、`Time`（Unix 秒）和 `Count`（項目被開啟的次數）欄位。`HistoryService.GetHistoryEntries()` 按最近開啟順序返回記錄。

### 元件啟用狀態與高成本執行階段

`PluginSettingsService.IsComponentEnabled(...)` 用於讀取宿主儲存的元件級開關。擁有目錄監聽器、背景工作執行緒、外部執行階段或其他高成本狀態的元件，應在初始化這些狀態前先檢查開關，並訂閱 `ComponentEnablementChanged`，在使用者切換開關後啟動或停止對應執行階段。如果宿主沒有註冊回調或回調失敗，此方法會返回 `true`，確保外掛模組在未接入完整宿主時仍可使用。

## 2. Shell 原生檔案操作封裝

`Lertaro.PluginSdk.Shell.FileOperations` 封裝了 Windows Shell 原生的 `IFileOperation` 介面。外掛模組執行檔案移動、複製與刪除時，使用者將獲得與檔案總管完全一致的原生進度對話方塊、衝突替換提示與 `Ctrl+Z` 復原支援：

```csharp
namespace Lertaro.PluginSdk.Shell.FileOperations;

// 批次貼上或移動（合併為單次 Shell 操作）。`move` 沒有預設值：請說明你要哪一個。
public static class ShellPasteHelper
{
    public static void PasteAsync(
        IReadOnlyList<string> sourcePaths,
        string destinationFolder,
        bool move,
        Action? onCompleted = null);
}

// 安全放入資源回收筒或永久刪除。`permanent` 同樣沒有預設值。
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
    public static bool HasVirtualFiles(IDataObject? data);                        // 是否有 FileGroupDescriptorW
    public static List<string> Extract(IDataObject? data, string targetFolder);   // 同步；回傳它寫入的檔案
    public static string? ResolveDestination(string targetFolder, string name);   // 空白輸入時返回 null；重名自動加 (2)
}
```

這三個操作輔助類別全都是**一觸即擲的 `void`**，而不是 `Task`：`PasteAsync` 接受一個選填的 `onCompleted` 回呼，其餘的則不回報任何東西。空的或全為空白的引數集會被忽略，而不會擲出例外。

> [!TIP]
> 這些輔助類別會自行封送至 **SDK 自己擁有、在你自己的程序內啟動**的 STA 背景工作執行緒（`ShellOperationStaWorker`——它是 SDK 內部實作，不是需要你註冊或設定的東西），因此外掛模組執行 Shell 操作時不必自行管理 COM 執行緒套間。刻意不提供取消、也不提供可供 await 的結果：原生的確認與進度對話方塊就是那個互動過程，而需要知道結果的外掛模組應在事後讀取目標位置。

## 3. 應用程式生命週期與佈景主題化外掛模組視窗

`AppLifecycleService.RequestRestart()` 會要求主機應用程式執行優雅重新啟動。主機會啟動替代程序，等待目前執行個體完成正常結束後再退出；外掛模組不需要自行啟動可執行檔或關閉主機。主機接受要求時此方法會回傳 `true`。

對於外掛模組自有的 WPF 內容，`Lertaro.PluginSdk.Windows.PluginWindow` 提供統一的圓角佈景主題視窗框架。將外掛模組檢視指派給 `ContentHostControl.Content`，並透過 `Footer` 加入底部按鈕。一般工作列視窗使用 `PluginWindowMode.Window`；需要置頂且從 Alt+Tab 隱藏的對話方塊使用 `PluginWindowMode.Dialog`。不傳入圖示時會使用主機的預設應用程式圖示。

```csharp
var window = new PluginWindow("我的工具", 720, 470, PluginWindowMode.Dialog);
window.ContentHostControl.Content = new MyView();
window.Footer.Children.Add(new Button { Content = "確定", IsDefault = true });
window.ShowDialog();
```

`PluginWindow.ShowFooter` 會在工具沒有任何按鈕時關閉底部列；省略圖示引數時會回退到宿主的預設應用程式圖示。

## 4. 視窗、查詢、主題與預覽基礎設施

| 宿主服務 | 核心方法與簽章 | 功能說明 |
| :--- | :--- | :--- |
| **`SearchWindowService`** | `bool IsWindowVisible()`<br>`void ShowWindow(string? query = null)`<br>`void HideWindow()`<br>`void FocusQueryTextBox()` | 從外掛模組查詢並驅動宿主的搜尋視窗，包括交給它一個起始查詢。 |
| **`SearchQueryService`** | `void ChangeQuery(string query, bool requery = false)`<br>`string StripQueryTokens(string query)` | 寫入活動視窗的查詢、可選擇性地重新執行搜尋，並移除宿主的後綴 token，讓外掛模組看到使用者實際輸入的純文字。 |
| **`ThemeService`** | `bool IsDarkTheme` | 一個供自行繪製內容的外掛模組使用的旗標。當宿主委派不存在時，它會讀取執行中 WPF 應用程式的資源，因此在啟動器之外繪製的外掛模組仍能取得答案，而不會擲出例外。 |
| **`PluginPreviewCache`** | `string Register(string title, string pluginName, Lazy<UserControl> factory, Func<object?>? iconProvider = null)`<br>`PluginPreviewEntry? GetEntry(string key)`<br>`UIElement? GetPreview(string key)` | 以惰性方式註冊一個外掛模組自有的預覽控制項，並把查找用的金鑰交給宿主，因此該控制項是在它第一次真正被顯示時才建立，而不是在載入時。 |
| **`PreviewActivationSignal`** | `void Begin()`<br>`void End()`<br>`bool IsActive`<br>`event Action? FocusStolen` / `void NotifyFocusStolen()` | 只要一個**外部程序**的原生處理常式正在被主動宿主，它就保持設定——是整個檢視工作階段，而不只是它的冷啟動，因為與它渲染出的內容互動可能彈出真正的頂層視窗。巢狀的 `Begin`/`End` 深度會被計數，因此提供者必須成對呼叫它們。*目前是宿主的行為，並非契約：* 訊號保持設定期間，搜尋視窗不會把自己的失焦當成「使用者點到別處了」。 |
| **`PreviewDialogSignal`** | `void NotifyDialogOpened()`<br>`void NotifyDialogClosed()`<br>`event Action? DialogOpened` / `DialogClosed` | 由提供者在原生處理常式自己的彈出視窗出現時觸發（被預覽的加密檔案所彈出的 Word「輸入密碼」提示，就是它存在的理由）。*目前是宿主的行為，並非契約：* 該對話方塊存在期間會隱藏快速視窗及其預覽，好讓對話方塊能被操作，之後再把它們還原。 |
| **`LocalSendTransferService`** | `void OpenSendWindow(IReadOnlyList<string>? files, string? text)` | 開啟宿主的 LocalSend 視窗並預先載入一個檔案清單或一段文字內容，讓外掛模組能在不擁有任何 UI 的情況下交辦一次傳輸。 |
| **`ToolRunService`** | `Func<string, string, Task<string?>>? RunDopusPathsFunc`（由宿主指派） | 在一個真正能被它回應的程序中執行外部工具。宿主的鍵盤鉤子是以系統管理員權限執行，而一個提權的 `dopusrt.exe` 永遠無法被未提權的 Directory Opus 回應（UIPI 會阻擋回應），但降權一個程序則需要鉤子的權杖所沒有的權限。App 以使用者自己的權限層級執行，因此鉤子會把請求轉送到那裡。讀取這個委派並把 `null` 視為不可用；呼叫端必須先建立輸出檔案，因為工具會填進一個已存在的檔案。 |
